using System;
using System.Threading.Tasks;

namespace MamboDMA.Services
{
    public static class VmmService
    {
        public static void InitOnly()
        {
            JobSystem.Schedule(() =>
            {
                try
                {
                    MamboDMA.DmaMemory.InitOnly("fpga", applyMMap: true);
                    Snapshots.Mutate(s => s with { Status = "VMM ready", VmmReady = true });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "VMM init failed: " + ex.Message, VmmReady = false });
                }
            });
        }

        public static void Attach(string exe)
        {
            JobSystem.Schedule(() =>
            {
                try
                {
                    if (MamboDMA.DmaMemory.TryAttachOnce(exe, out var err))
                    {
                        Snapshots.Mutate(s => s with
                        {
                            Status = $"Attached PID={MamboDMA.DmaMemory.Pid} base=0x{MamboDMA.DmaMemory.Base:X}",
                            VmmReady = true,
                            Pid = (int)MamboDMA.DmaMemory.Pid,
                            MainBase = MamboDMA.DmaMemory.Base
                        });
                    }
                    else
                    {
                        Snapshots.Mutate(s => s with { Status = "Attach failed: " + err });
                    }
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Attach error: " + ex.Message });
                }
            });
        }

        public static void RefreshProcesses()
        {
            JobSystem.Schedule(() =>
            {
                try
                {
                    var list = MamboDMA.DmaMemory.GetProcessList(); // List<ProcEntry>
                    Snapshots.Mutate(s => s with
                    {
                        Processes = (list != null) ? list.ToArray()
                                                   : Array.Empty<MamboDMA.DmaMemory.ProcEntry>()
                    });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Proc list error: " + ex.Message });
                }
            });
        }

        public static void RefreshModules()
        {
            JobSystem.Schedule(() =>
            {
                if (!MamboDMA.DmaMemory.IsVmmReady)
                {
                    // Optional: only set status once or throttle
                    Snapshots.Mutate(s => s with { Status = "Init VMM first to enumerate processes." });
                    return;
                }
                try
                {
                    var mods = MamboDMA.DmaMemory.GetModules(); // List<ModuleInfo>
                    Snapshots.Mutate(s => s with
                    {
                        Modules = (mods != null) ? mods.ToArray()
                                                 : Array.Empty<MamboDMA.DmaMemory.ModuleInfo>()
                    });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Modules error: " + ex.Message });
                }
            });
        }

        /// <summary>Dispose VMM + clear related state (safe to call repeatedly).</summary>
        public static void DisposeVmm()
        {
            JobSystem.Schedule(() =>
            {
                if (!MamboDMA.DmaMemory.IsVmmReady)
                {
                    // Optional: only set status once or throttle
                    Snapshots.Mutate(s => s with { Status = "Init VMM first to enumerate processes." });
                    return;
                }
                try
                {
                    MamboDMA.DmaMemory.Dispose();
                    Snapshots.Mutate(s => s with
                    {
                        Status = "Disposed.",
                        VmmReady = false,
                        Processes = Array.Empty<MamboDMA.DmaMemory.ProcEntry>(),
                        Modules   = Array.Empty<MamboDMA.DmaMemory.ModuleInfo>(),
                        Pid = 0,
                        MainBase = 0
                    });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Dispose error: " + ex.Message });
                }
            });
        }
    }
}
