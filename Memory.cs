using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using MamboDMA.Input;
using VmmSharpEx;
using VmmSharpEx.Scatter;
using VFlags = VmmSharpEx.Options.VmmFlags;

namespace MamboDMA;

public static class DmaMemory
{
    #region Fields / Properties

    private static Vmm? _vmm;
    public static Vmm Vmm => _vmm ?? throw new InvalidOperationException("VMM not initialized.");
    public static bool IsVmmReady => _vmm is not null;

    public static uint Pid { get; private set; }
    public static ulong Base { get; private set; }
    public static bool IsAttached => _vmm is not null && Pid != 0 && Base != 0;

    #endregion

    #region Init / Attach / Dispose

    /// <summary>Initialize VMM only (no attach). Applies memory map if requested. Safe to call multiple times.</summary>
    public static void InitOnly(string device = "fpga", bool applyMMap = true)
    {
        _vmm ??= new Vmm(new[] { "-printf", "-v", "-device", device, "-waitinitialize" });
        if (applyMMap)
        {
            try { _ = _vmm.GetMemoryMap(applyMap: true, outputFile: "mmap.txt"); } catch { /* best-effort */ }
        }
    }

    /// <summary>Re-apply memory map on an already initialized VMM.</summary>
    public static void RefreshMemoryMap(string output = "mmap.txt")
    {
        if (_vmm is null) return;
        try { _ = _vmm.GetMemoryMap(applyMap: true, outputFile: output); } catch { }
    }

    /// <summary>Initializes VMM (if needed) and blocks until the PID/module base are found.</summary>
    public static void Attach(string exeName, string? moduleName = null, string device = "fpga", bool applyMMap = true)
    {
        _vmm ??= new Vmm(new[] { "-printf", "-v", "-device", device, "-waitinitialize" });
        if (applyMMap)
        {
            try { _ = _vmm.GetMemoryMap(applyMap: true, outputFile: "mmap.txt"); } catch { /* best-effort */ }
        }

        Pid = WaitForPidByName(exeName);
        Base = WaitForModuleBase(Pid, moduleName ?? exeName);
    }

    /// <summary>Non-blocking probe: returns true if PID & Base resolved right now.</summary>
    public static bool TryAttachOnce(string exeName, out string error, string? moduleName = null, string device = "fpga", bool applyMMap = true)
    {
        error = "";
        try
        {
            _vmm ??= new Vmm(new[] { "-printf", "-v", "-device", device, "-waitinitialize" });
            if (applyMMap) { try { _ = _vmm.GetMemoryMap(applyMap: true, outputFile: "mmap.txt"); } catch { } }

            if (!_vmm.PidGetFromName(exeName, out var pid) || pid == 0) { error = "PID not found."; return false; }

            var baseAddr = _vmm.ProcessGetModuleBase(pid, moduleName ?? exeName);
            if (baseAddr == 0) { error = "Module base not found."; return false; }

            Pid = pid; Base = baseAddr;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static void Dispose()
    {
        try { _vmm?.Dispose(); } catch { }
        _vmm = null; Pid = 0; Base = 0;
    }

    #endregion

    #region Process Enumeration

    public readonly record struct ProcEntry(uint Pid, string Name, bool IsWow64);

    /// <summary>Returns (Pid, Name, IsWow64) for all running processes.</summary>
    public static List<ProcEntry> GetProcessList()
    {
        if (_vmm is null) throw new InvalidOperationException("VMM not initialized.");

        try
        {
            var infos = _vmm.ProcessGetInformationAll(); // ProcessInfo[]
            if (infos is null || infos.Length == 0) return new();

            return infos
                .Select(pi => new ProcEntry(pi.dwPID, pi.sNameLong ?? string.Empty, pi.fWow64))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Pid)
                .ToList();
        }
        catch
        {
            // Fallback: enumerate PIDs and try to pull main module name.
            var list = new List<ProcEntry>();
            var pids = _vmm.PidGetList() ?? Array.Empty<uint>();
            foreach (var pid in pids)
            {
                try
                {
                    var mods = _vmm.Map_GetModule(pid, fExtendedInfo: false);
                    var name = (mods != null && mods.Length > 0 && !string.IsNullOrEmpty(mods[0].sText))
                               ? mods[0].sText
                               : $"pid_{pid}";
                    list.Add(new ProcEntry(pid, name, false));
                }
                catch { /* skip PID on error */ }
            }
            return list
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Pid)
                .ToList();
        }
    }

    #endregion

    #region Module Enumeration

    public sealed class ModuleInfo
    {
        public string Name { get; init; } = "";
        public string FullName { get; init; } = "";
        public ulong Base { get; init; }
        public ulong Size { get; init; }
        public bool IsWow64 { get; init; }
    }

    /// <summary>List loaded modules for a PID (default = currently attached).</summary>
    public static List<ModuleInfo> GetModules(uint? pid = null, bool includeWow64Flag = false)
    {
        if (_vmm == null) throw new InvalidOperationException("VMM not initialized.");
        uint p = pid ?? Pid;
        if (p == 0) return new();

        var arr = _vmm.Map_GetModule(p, fExtendedInfo: false);
        if (arr == null || arr.Length == 0) return new();

        var list = new List<ModuleInfo>(arr.Length);
        foreach (var m in arr)
        {
            var name = string.IsNullOrWhiteSpace(m.sFullName)
                ? (m.sText ?? "")
                : Path.GetFileName(m.sFullName);

            list.Add(new ModuleInfo
            {
                Name = name ?? "",
                FullName = m.sFullName ?? m.sText ?? "",
                Base = m.vaBase,
                Size = m.cbImageSize,
                IsWow64 = m.fWow64
            });
        }

        return list
            .OrderBy(mi => mi.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mi => mi.Base)
            .ToList();
    }

    #endregion

    #region Read / Write Helpers

    public static bool Read<T>(ulong address, out T value) where T : unmanaged
        => Vmm.MemReadValue(Pid, address, out value);

    public static T Read<T>(ulong address) where T : unmanaged
    {
        Vmm.MemReadValue(Pid, address, out T v);
        return v;
    }
    public static T[]? ReadArray<T>(ulong address, int count) where T : unmanaged
        => Vmm.MemReadArray<T>(Pid, address, count);
    /// <summary>Read raw bytes. Returns null on failure. If fewer bytes were read than requested, the array is resized.</summary>
    public static byte[]? ReadBytes(ulong address, uint size, VmmFlags flags = VmmFlags.NONE)
    {
        if (size == 0) return Array.Empty<byte>();
        var buf = new byte[size];
        unsafe
        {
            fixed (byte* p = buf)
            {
                if (!Vmm.MemRead(Pid, address, (nint)p, size, out var read, (VFlags)flags) || read == 0)
                    return null;
                if (read < size) Array.Resize(ref buf, (int)read);
            }
        }
        return buf;
    }

    /// <summary>Read a zero-terminated ASCII string (best-effort). Returns empty string on failure.</summary>
    public static string ReadAsciiZ(ulong address, int max = 256, VmmFlags flags = VmmFlags.NONE)
    {
        if (max <= 0) max = 1;
        var bytes = ReadBytes(address, (uint)max, flags);
        if (bytes is null || bytes.Length == 0) return "";
        int zero = Array.IndexOf(bytes, (byte)0);
        if (zero >= 0) return Encoding.ASCII.GetString(bytes, 0, zero);
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>Read a zero-terminated UTF-16 (Unicode) string (useful if names show as "?????").</summary>
    public static string ReadUtf16Z(ulong address, int maxChars = 256, VmmFlags flags = VmmFlags.NONE)
    {
        if (maxChars <= 0) maxChars = 1;
        var bytes = ReadBytes(address, (uint)(maxChars * 2), flags);
        if (bytes is null || bytes.Length == 0) return "";
        int end = -1;
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
        }
        if (end < 0) end = bytes.Length - (bytes.Length % 2);
        return Encoding.Unicode.GetString(bytes, 0, end);
    }

    public static string? ReadUnicodeZ(ulong address, int max = 256)
        => Vmm.MemReadString(Pid, address, max * 2, Encoding.Unicode);
    public static string? ReadUtf32Z(ulong address, int max = 256)
        => Vmm.MemReadString(Pid, address, max * 2, Encoding.UTF32);
    public static string? ReadBigUnicodeZ(ulong address, int max = 256)
        => Vmm.MemReadString(Pid, address, max * 2, Encoding.BigEndianUnicode);

    public static bool Write<T>(ulong address, in T value) where T : unmanaged
        => Vmm.MemWriteValue(Pid, address, value);

    #endregion
    #region Scatter Helpers
    
    /// <summary>Create a new scatter map bound to the current game PID.</summary>
    public static ScatterReadMap Scatter()
    {
        if (_vmm is null) throw new InvalidOperationException("VMM not initialized.");
        if (Pid == 0) throw new InvalidOperationException("Not attached to a process.");
        return new ScatterReadMap(_vmm, Pid);
    }
    
    /// <summary>
    /// Execute a single scatter round defined by <paramref name="define"/>.
    /// Example:
    /// <code>
    /// DmaMemory.ScatterRound(rd => {
    ///     rd.Add(out ulong localPawn, baseAddr + offs.LocalPawn);
    ///     rd.AddBuffer(nameBuf, namePtr, 64);
    /// });
    /// </code>
    /// </summary>
    public static void ScatterRound(Action<ScatterReadRound> define, bool useCache = false)
    {
        if (_vmm is null) throw new InvalidOperationException("VMM not initialized.");
        if (Pid == 0) throw new InvalidOperationException("Not attached to a process.");
    
        using var map = new ScatterReadMap(_vmm, Pid);
        var rd = map.AddRound(useCache);
        define(rd);
        map.Execute();
    }
    
    /// <summary>
    /// Follow a pointer chain with direct reads: result = (((base + o0)-> + o1)-> + ...).
    /// Returns false if any hop fails or resolves to 0.
    /// </summary>
    public static bool TryFollow(ulong baseAddr, ReadOnlySpan<ulong> offsets, out ulong result)
    {
        result = baseAddr;
        foreach (var off in offsets)
        {
            // add offset then deref
            ulong nextAddr = result + off;
            if (!Read(nextAddr, out result) || result == 0)
                return false;
        }
        return true;
    }
    
    #endregion
    #region Private Wait Helpers

    private static uint WaitForPidByName(string exe)
    {
        while (true)
        {
            if (Vmm.PidGetFromName(exe, out var pid) && pid != 0) return pid;
            Thread.Sleep(300);
        }
    }

    private static ulong WaitForModuleBase(uint pid, string module)
    {
        while (true)
        {
            var b = Vmm.ProcessGetModuleBase(pid, module);
            if (b != 0) return b;
            Thread.Sleep(300);
        }
    }

    #endregion

    #region IVmmEx Adapter

    public sealed class VmmSharpExAdapter : IVmmEx
    {
        private readonly Vmm _vmm;
        public VmmSharpExAdapter(Vmm vmm) => _vmm = vmm ?? throw new ArgumentNullException(nameof(vmm));

        #region Registry

        public string RegReadString(string path)
        {
            var bytes = _vmm.WinReg_QueryValue(path, out uint type);
            if (bytes == null || bytes.Length == 0) return string.Empty;

            if (type == 1 || type == 2) // REG_SZ / REG_EXPAND_SZ
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');

            if (type == 7 || type == 3) // REG_MULTI_SZ / REG_BINARY (lenient)
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');

            return string.Empty;
        }

        public uint RegReadDword(string path)
        {
            var bytes = _vmm.WinReg_QueryValue(path, out uint type);
            if (bytes == null || bytes.Length == 0) return 0;

            if (type == 4 && bytes.Length >= 4) // REG_DWORD
                return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));

            if (type == 1 || type == 2)
            {
                var s = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                return uint.TryParse(s, out var v) ? v : 0;
            }
            return 0;
        }

        #endregion

        #region Process / Module Info

        public uint GetPidByName(string name)
            => _vmm.PidGetFromName(name, out var pid) && pid != 0 ? pid : 0;

        public uint[] GetPidsByName(string name)
        {
            var pids = _vmm.PidGetList();
            if (pids == null || pids.Length == 0) return Array.Empty<uint>();

            var mod1 = name;
            var mod2 = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : (name + ".exe");

            return pids.Where(pid =>
            {
                var b1 = _vmm.ProcessGetModuleBase(pid, mod1);
                if (b1 != 0) return true;
                var b2 = _vmm.ProcessGetModuleBase(pid, mod2);
                return b2 != 0;
            }).ToArray();
        }

        public bool GetModuleInfo(uint pid, string moduleName, out ulong baseAddress, out ulong imageSize)
        {
            baseAddress = _vmm.ProcessGetModuleBase(pid, moduleName);
            if (baseAddress == 0) { imageSize = 0; return false; }

            Span<byte> hdr = stackalloc byte[0x1000];
            unsafe
            {
                fixed (byte* p = hdr)
                {
                    if (!_vmm.MemRead(pid, baseAddress, (nint)p, (uint)hdr.Length, out var read, VFlags.NOCACHE) || read < 0x400)
                    {
                        imageSize = GuessSize(moduleName);
                        return true;
                    }
                }
            }

            if (hdr[0] != (byte)'M' || hdr[1] != (byte)'Z')
            {
                imageSize = GuessSize(moduleName);
                return true;
            }

            int e_lfanew = BinaryPrimitives.ReadInt32LittleEndian(hdr.Slice(0x3C, 4));
            if (e_lfanew <= 0 || e_lfanew + 0x100 > hdr.Length) { imageSize = GuessSize(moduleName); return true; }

            if (hdr[e_lfanew] != (byte)'P' || hdr[e_lfanew + 1] != (byte)'E')
            { imageSize = GuessSize(moduleName); return true; }

            int optOff = e_lfanew + 24;
            if (optOff + 0x38 + 4 > hdr.Length) { imageSize = GuessSize(moduleName); return true; }

            uint sz = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(optOff + 0x38, 4));
            imageSize = sz != 0 ? sz : GuessSize(moduleName);
            return true;
        }

        private static ulong GuessSize(string module)
            => module.Contains("win32k", StringComparison.OrdinalIgnoreCase) ? 0x800000UL : 0x400000UL;

        #endregion

        #region Memory / Scan / Symbols

        public unsafe bool MemRead(uint pid, ulong va, nint pb, uint cb, out uint cbRead, VmmFlags flags)
            => _vmm.MemRead(pid, va, pb, cb, out cbRead, (VFlags)flags);

        public ulong FindSignature(uint pid, ulong start, ulong end, string idaStylePattern)
        {
            if (end <= start) return 0;

            var (needle, mask) = ParsePattern(idaStylePattern);
            if (needle.Length == 0) return 0;

            const int chunk = 256 * 1024;
            int overlap = Math.Max(needle.Length - 1, 0);

            byte[] buf = new byte[chunk + overlap];

            ulong cur = start;
            int valid = 0;

            while (cur < end)
            {
                int toRead = (int)Math.Min((ulong)chunk, end - cur);
                unsafe
                {
                    fixed (byte* p = buf)
                    {
                        if (!_vmm.MemRead(pid, cur, (nint)p, (uint)toRead, out var read, VFlags.NOCACHE | VFlags.NO_PREDICTIVE_READ))
                            return 0;
                        valid = (int)read;
                    }
                }
                if (valid <= 0) return 0;

                int limit = valid - needle.Length + 1;
                for (int i = 0; i < limit; i++)
                {
                    if (Match(buf, i, needle, mask))
                        return cur + (uint)i;
                }

                cur += (uint)limit;
                if (cur >= end) break;

                Buffer.BlockCopy(buf, valid - overlap, buf, 0, overlap);
            }

            return 0;
        }

        private static (byte[] bytes, bool[] mask) ParsePattern(string pattern)
        {
            var parts = pattern.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>(parts.Length);
            var mask = new List<bool>(parts.Length);

            foreach (var p in parts)
            {
                if (p == "?" || p == "??") { bytes.Add(0); mask.Add(false); continue; }
                if (byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                { bytes.Add(b); mask.Add(true); }
            }
            return (bytes.ToArray(), mask.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Match(byte[] hay, int offset, byte[] needle, bool[] mask)
        {
            for (int i = 0; i < needle.Length; i++)
                if (mask[i] && hay[offset + i] != needle[i]) return false;
            return true;
        }

        public ulong GetExportVA(uint pid, string moduleName, string function)
        {
            var kernelPid = pid | IVmmEx.PID_PROCESS_WITH_KERNELMEMORY;
            var entries = _vmm.Map_GetEAT(kernelPid, moduleName, out var eatInfo);
            if (entries == null || entries.Length == 0 || !eatInfo.fValid)
                return 0;

            foreach (var e in entries)
            {
                if (string.Equals(e.sFunction, function, StringComparison.Ordinal))
                    return e.vaFunction;
            }
            return 0;
        }

        public bool PdbSymbolAddress(uint pid, string moduleName, string symbol, out ulong va)
        {
            _ = _vmm.ProcessGetModuleBase(pid, moduleName);
            var ok = _vmm.PdbSymbolAddress(moduleName, symbol, out va);
            return ok && va != 0;
        }

        #endregion
    }

    #endregion
}
