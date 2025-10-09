// MamboDMA/Games/ABI/ABIGame.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using MamboDMA.Services;
using MamboDMA.Input;
using static MamboDMA.Misc;

namespace MamboDMA.Games.ABI
{
    /// <summary>
    /// Arena Breakout Infinite integration for MamboDMA:
    /// - Progressive neck aimbot
    /// - Makcu or Win32 mouse fallback
    /// - Debug DMA read inspector
    /// - FName + CharacterType decryption
    /// </summary>
    public sealed class ABIGame : IGame
    {
        public string Name => "ArenaBreakoutInfinite";

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Runtime state
        private static List<ABIEntity> _entities = new();
        private static bool _running;
        private static CameraInfo _cam;
        private static CancellationTokenSource _cts = new();

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Settings
        private static float _aimFovDeg = 60f;
        private static float _aimSmooth = 6.0f;
        private static int _aimKeyVk = 0x01;
        private static bool _showBoxESP = true;
        private static bool _showLineToEnemy = true;
        private static bool _showDistance = true;
        private static bool _useMakcuWhenAvailable = true;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Debug overlay
        private static bool _showDebug = false;
        private static readonly List<(string Name, string Value, bool Ok)> _debugReads = new();
        private static readonly object _debugLock = new();
        private static double _lastUpdateMs = 0;

        private static void LogDebug(string name, string value, bool ok)
        {
            lock (_debugLock)
            {
                _debugReads.Add((name, value, ok));
                if (_debugReads.Count > 300)
                    _debugReads.RemoveAt(0);
            }
            Logger.Debug($"[DBG] {name} = {value} {(ok ? "" : "(fail)")}");
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Aimbot
        private static ulong _targetActor = 0;
        private static float _currentBestDist = float.MaxValue;

        private static readonly (int vk, string name)[] _keyOptions =
        {
            (0x01, "LButton"), (0x02, "RButton"), (0x04, "MButton"),
            (0x20, "Space"), (0x31, "1"), (0x32, "2"),
            (0x41, "A"), (0x42, "B"), (0x43, "C"),
            (0x70, "F1"), (0x71, "F2")
        };
        private static int _keyOptionIdx = 0;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public void Initialize() { }

        public void Start()
        {
            if (_running) return;
            _cts = new CancellationTokenSource();

            JobSystem.Schedule(async _ =>
            {
                Logger.Info("[ABI] world polling started.");
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (!DmaMemory.IsAttached)
                        {
                            await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                            continue;
                        }
                        var sw = Stopwatch.StartNew();
                        UpdateWorld();
                        sw.Stop();
                        _lastUpdateMs = sw.Elapsed.TotalMilliseconds;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { Logger.Warn($"[ABI] UpdateWorld exception: {ex.Message}"); }
                    try { await Task.Delay(100, _cts.Token).ConfigureAwait(false); } catch { break; }
                }
                Logger.Info("[ABI] world polling stopped.");
            });

            _running = true;
        }

        public void Stop()
        {
            if (!_running) return;
            try { _cts.Cancel(); } catch { }
            _running = false;
        }

        public void Tick() { }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public void Draw(ImGuiWindowFlags flags)
        {
            var dl = ImGui.GetForegroundDrawList();
            var scr = ScreenService.Current;
            var center = new Vector2(scr.W / 2f, scr.H / 2f);

            foreach (var e in _entities)
            {
                if (!ABIMath.WorldToScreen(e.Position, out var s, _cam, scr.W, scr.H))
                    continue;
                uint color = ImGui.ColorConvertFloat4ToU32(e.Visible ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1));
                dl.AddCircleFilled(new Vector2(s.X, s.Y), 4, color);
                if (_showLineToEnemy)
                    dl.AddLine(center, new Vector2(s.X, s.Y), color, 1.0f);
                if (_showBoxESP)
                {
                    float w = 40, h = 70;
                    dl.AddRect(new Vector2(s.X - w / 2, s.Y - h / 2), new Vector2(s.X + w / 2, s.Y + h / 2), color, 2f);
                }
                if (_showDistance)
                {
                    string text = $"{e.TypeTag} {e.Name}";
                    ImGui.GetForegroundDrawList().AddText(new Vector2(s.X + 6, s.Y), color, text);
                }
            }

            // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
            ImGui.SetNextWindowBgAlpha(0.4f);
            ImGui.Begin("ABI Overlay", flags | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize);

            ImGui.Text($"Entities: {_entities.Count}");
            ImGui.Text($"Attached: {DmaMemory.IsAttached}");
            ImGui.Text($"Update: {_lastUpdateMs:F1} ms");
            ImGui.Separator();
            ImGui.Checkbox("Debug DMA Overlay", ref _showDebug);
            ImGui.Separator();

            ImGui.Text("Aimbot:");
            ImGui.Checkbox("Use Makcu Movement", ref _useMakcuWhenAvailable);
            ImGui.SliderFloat("FOV (deg)", ref _aimFovDeg, 5f, 180f);
            ImGui.SliderFloat("Smooth", ref _aimSmooth, 1f, 50f);

            string preview = _keyOptions[_keyOptionIdx].name;
            if (ImGui.BeginCombo("Aim Key", preview))
            {
                for (int i = 0; i < _keyOptions.Length; i++)
                {
                    bool sel = (i == _keyOptionIdx);
                    if (ImGui.Selectable(_keyOptions[i].name, sel))
                    {
                        _keyOptionIdx = i;
                        _aimKeyVk = _keyOptions[i].vk;
                    }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Checkbox("Box ESP", ref _showBoxESP);
            ImGui.Checkbox("Line ESP", ref _showLineToEnemy);
            ImGui.Checkbox("Show Distance/Names", ref _showDistance);

            ImGui.Separator();
            if (ImGui.Button(_running ? "Stop" : "Start"))
                if (_running) Stop(); else Start();

            ImGui.End();

            if (_showDebug) DrawDebugWindow();
            HandleAimbotFrame(center, scr.W, scr.H);
        }

        private static void DrawDebugWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(520, 420), ImGuiCond.FirstUseEver);
            ImGui.Begin("ABI Debug Read Monitor", ImGuiWindowFlags.NoCollapse);
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), $"DMA Reads ¡ª {_lastUpdateMs:F1} ms");
            ImGui.Separator();
            ImGui.BeginChild("dbg_scroll", new Vector2(0, 0));
            lock (_debugLock)
            {
                foreach (var (name, val, ok) in _debugReads)
                {
                    var color = ok ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0.3f, 0.3f, 1);
                    ImGui.TextColored(color, $"{name} = {val}");
                }
            }
            ImGui.EndChild();
            ImGui.End();
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static void UpdateWorld()
        {
            try
            {
                lock (_debugLock) _debugReads.Clear();
                ulong module = DmaMemory.Base;
                LogDebug("Base", $"0x{module:X}", module != 0);
                if (module == 0) return;

                ulong gworld = TryReadU64(module + ABIOffsets.GWorld, "GWorld");
                if (gworld == 0) return;
                ulong ulevel = TryReadU64(gworld + ABIOffsets.PersistentLevel, "ULevel");
                ulong actorsArray = TryReadU64(ulevel + ABIOffsets.ActorsOffset, "ActorsArray");
                int actorCount = TryReadI32(ulevel + ABIOffsets.ActorSize, "ActorCount");

                ulong gameInst = TryReadU64(gworld + ABIOffsets.OwningGameInstance, "GameInstance");
                ulong localPlayers = TryReadU64(gameInst + ABIOffsets.LocalPlayers, "LocalPlayers");
                ulong localPlayer = TryReadU64(localPlayers, "LocalPlayer");
                ulong playerController = TryReadU64(localPlayer + ABIOffsets.PlayerController, "PlayerController");
                ulong camMgr = TryReadU64(playerController + ABIOffsets.CameraManager, "CameraManager");

                ulong camCache = camMgr + ABIOffsets.CameraCacheOffset;
                Vector3 camPos = TryReadVec3(camCache, "CamPos");
                Vector3 camRot = TryReadVec3(camCache + 12, "CamRot");
                _cam = new CameraInfo { Position = camPos, Rotation = camRot, Fov = 90f };

                var tmp = new List<ABIEntity>(actorCount);
                for (int i = 0; i < actorCount; i++)
                {
                    ulong actor = TryReadU64(actorsArray + (ulong)(i * 8), $"Actor[{i}]");
                    if (actor == 0) continue;
                    ulong mesh = TryReadU64(actor + ABIOffsets.Mesh, $"Mesh[{i}]");
                    if (mesh == 0) continue;
                    ulong root = TryReadU64(actor + ABIOffsets.RootComponent, $"Root[{i}]");
                    Vector3 pos = TryReadVec3(root + ABIOffsets.RelativeLocation, $"Pos[{i}]");
                    bool vis = IsVisible(mesh);

                    // CharacterType & Name
                    sbyte typeVal = DmaMemory.Read<sbyte>(actor + ABIOffsets.CachedCharacterType);
                    var type = (ECharacterType)typeVal;
                    string tag = ABICharacterResolver.GetTypeName(type);

                    // Optional name (using decrypted GNames)
                    string actorName = ABINameResolver.GetActorNameByIndex(i);

                    tmp.Add(new ABIEntity
                    {
                        Ptr = actor,
                        Mesh = mesh,
                        Position = pos,
                        Visible = vis,
                        Name = actorName,
                        TypeTag = tag
                    });
                }

                _entities = tmp;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ABI] UpdateWorld error: {ex.Message}");
            }
        }

        private static ulong TryReadU64(ulong addr, string name)
        {
            try { var v = DmaMemory.Read<ulong>(addr); LogDebug(name, $"0x{v:X}", true); return v; }
            catch { LogDebug(name, "ERR", false); return 0; }
        }
        private static int TryReadI32(ulong addr, string name)
        {
            try { var v = DmaMemory.Read<int>(addr); LogDebug(name, v.ToString(), true); return v; }
            catch { LogDebug(name, "ERR", false); return 0; }
        }
        private static Vector3 TryReadVec3(ulong addr, string name)
        {
            try { var v = DmaMemory.Read<Vector3>(addr); LogDebug(name, $"({v.X:F1},{v.Y:F1},{v.Z:F1})", true); return v; }
            catch { LogDebug(name, "ERR", false); return Vector3.Zero; }
        }

        private static bool IsVisible(ulong mesh)
        {
            try
            {
                float sub = DmaMemory.Read<float>(mesh + ABIOffsets.LastSubmitTime);
                float ren = DmaMemory.Read<float>(mesh + ABIOffsets.LastRenderTime);
                bool vis = ren + 0.06f >= sub;
                LogDebug($"Vis(mesh=0x{mesh:X})", vis.ToString(), true);
                return vis;
            }
            catch { LogDebug($"Vis(mesh=0x{mesh:X})", "ERR", false); return false; }
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static void HandleAimbotFrame(Vector2 center, float screenW, float screenH)
        {
            _targetActor = 0;
            _currentBestDist = float.MaxValue;
            float pixelRadius = _aimFovDeg;
            if (_cam.Fov > 0)
            {
                float ratio = (float)(Math.Tan(_aimFovDeg * Math.PI / 360.0) / Math.Tan(_cam.Fov * Math.PI / 360.0));
                pixelRadius = Math.Clamp(MathF.Abs(ratio) * (screenW / 2f), 8f, Math.Max(screenW, screenH));
            }

            foreach (var e in _entities)
            {
                if (!ABIMath.WorldToScreen(e.Position, out var s, _cam, screenW, screenH)) continue;
                float dx = s.X - center.X, dy = s.Y - center.Y, dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= pixelRadius && dist < _currentBestDist)
                {
                    _currentBestDist = dist;
                    _targetActor = e.Ptr;
                }
            }

            if (_targetActor == 0 || !Input.InputManager.IsKeyDown(_aimKeyVk)) return;

            try
            {
                ulong mesh = DmaMemory.Read<ulong>(_targetActor + ABIOffsets.Mesh);
                if (mesh == 0) return;
                Vector3 neck = GetBoneWorldPositionFallback(mesh, _targetActor, 15);
                if (!ABIMath.WorldToScreen(neck, out var t, _cam, screenW, screenH)) return;

                float dx = t.X - center.X, dy = t.Y - center.Y, dist = MathF.Sqrt(dx * dx + dy * dy);
                float exponent = 0.6f;
                float norm = MathF.Min(1f, dist / pixelRadius);
                float moveFactor = MathF.Pow(norm, exponent) / MathF.Max(0.001f, _aimSmooth);
                float moveX = dx * moveFactor, moveY = dy * moveFactor;

                if (Device.connected && _useMakcuWhenAvailable)
                    Device.move((int)moveX, (int)moveY);
                else
                    mouse_event(MOUSEEVENTF_MOVE, (int)moveX, (int)moveY, 0, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ABI] AimbotFrame error: {ex.Message}");
            }
        }

        private static Vector3 GetBoneWorldPositionFallback(ulong mesh, ulong actor, int boneIndex)
        {
            try
            {
                ulong boneArray = DmaMemory.Read<ulong>(mesh + ABIOffsets.BoneArrayOne);
                if (boneArray != 0)
                {
                    ulong entry = boneArray + (ulong)(boneIndex * 48);
                    Vector3 boneT = DmaMemory.Read<Vector3>(entry + 16);
                    ulong comp = DmaMemory.Read<ulong>(mesh + ABIOffsets.ComponentToWorld);
                    if (comp != 0)
                    {
                        Vector3 compT = DmaMemory.Read<Vector3>(comp + 16);
                        return boneT + compT;
                    }
                    return boneT;
                }
            }
            catch { }

            try
            {
                ulong root = DmaMemory.Read<ulong>(actor + ABIOffsets.RootComponent);
                if (root != 0)
                    return DmaMemory.Read<Vector3>(root + ABIOffsets.RelativeLocation);
            }
            catch { }

            return Vector3.Zero;
        }
    }

    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // Name + CharacterType Decryptors
    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    public static class ABINameResolver
    {
        private static ulong _secret;
        private static ulong _base => DmaMemory.Base;

        public static string GetActorNameByIndex(int index)
        {
            try
            {
                ulong namePool = _base + ABIOffsets.GNames;
                ulong poolLoc = DmaMemory.Read<ulong>(namePool + (ulong)(((index >> 16) + 2) * 8)) + (ulong)(2 * (ushort)index);
                ushort len = (ushort)(DmaMemory.Read<ushort>(poolLoc) >> 6);
                if (len <= 0 || len > 255) return string.Empty;
                var data = DmaMemory.ReadBytes(poolLoc + 2, len);
                return DecryptFName(data);
            }
            catch { return string.Empty; }
        }

        private static string DecryptFName(byte[] buf)
        {
            if (_secret == 0)
                _secret = DmaMemory.Read<ulong>(_base + ABIOffsets.DecBuffer);
            var sb = new StringBuilder(buf.Length);
            for (int i = 0; i < buf.Length; i++)
            {
                ulong tmp = _secret ^ ((_secret >> 3) & 0x4);
                tmp ^= (0x8 * (tmp & 0x4));
                char ch = (char)(tmp ^ buf[i] ^ ((tmp >> 3) & 0x4) ^ 0x5A);
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    public enum ECharacterType : sbyte
    {
        None = 0, PMC = 1, SCAV = 2, AI_SCAV = 3,
        AI_SCAV_BOSS = 4, AI_PMC = 5, AI_ELIT = 6,
        BOSS = 7, AI_SCAV_Follower = 8, MAX = 9
    }

    public static class ABICharacterResolver
    {
        public static string GetTypeName(ECharacterType id) => id switch
        {
            ECharacterType.None => "[None]",
            ECharacterType.PMC => "[PMC]",
            ECharacterType.SCAV => "[SCAV]",
            ECharacterType.AI_SCAV => "[AI_SCAV]",
            ECharacterType.AI_SCAV_BOSS => "[AI_SCAV_BOSS]",
            ECharacterType.AI_PMC => "[AI_PMC]",
            ECharacterType.AI_ELIT => "[AI_ELIT]",
            ECharacterType.BOSS => "[BOSS]",
            ECharacterType.AI_SCAV_Follower => "[AI_SCAV_Follower]",
            _ => "[?]"
        };
    }

    public struct ABIEntity
    {
        public ulong Ptr;
        public ulong Mesh;
        public Vector3 Position;
        public bool Visible;
        public string Name;
        public string TypeTag;
    }
}
