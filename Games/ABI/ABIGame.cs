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
    /// - Camera + entity inspector
    /// - Progressive neck aimbot
    /// - Makcu or Win32 mouse fallback
    /// </summary>
    public sealed class ABIGame : IGame
    {
        public string Name => "ArenaBreakoutInfinite";

        // 
        // Runtime state
        private static readonly List<ABIEntity> _entities = new();
        private static bool _running;
        private static CameraInfo _cam;
        private static CancellationTokenSource _cts = new();

        // 
        // Settings
        private static float _aimFovDeg = 60f;
        private static float _aimSmooth = 6.0f;
        private static int _aimKeyVk = 0x01;
        private static bool _showBoxESP = true;
        private static bool _showLineToEnemy = true;
        private static bool _showDistance = true;
        private static bool _useMakcuWhenAvailable = true;

        // 
        // Runtime stats
        private static double _lastUpdateMs = 0;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        // 
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
                    try { await Task.Delay(2, _cts.Token).ConfigureAwait(false); } catch { break; }
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

        // 
        public void Draw(ImGuiWindowFlags flags)
        {
            var dl = ImGui.GetForegroundDrawList();
            var scr = ScreenService.Current;
            var center = new Vector2(scr.W / 2f, scr.H / 2f);

            // ©¤©¤ Thread-safe snapshot copy
            List<ABIEntity> snapshot;
            lock (_entities)
                snapshot = _entities.Count > 0 ? new List<ABIEntity>(_entities) : new List<ABIEntity>();

            // ©¤©¤ Draw ESP
            foreach (var e in snapshot)
            {
                if (!e.TypeTag.Contains("AI_SCAV", StringComparison.OrdinalIgnoreCase) &&
                    !e.TypeTag.Contains("PMC", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ABIMath.WorldToScreen(e.Position, out var s, _cam, scr.W, scr.H))
                    continue;

                uint color = ImGui.ColorConvertFloat4ToU32(e.Visible
                    ? new Vector4(0.1f, 1f, 0.4f, 1)
                    : new Vector4(1f, 0.3f, 0.3f, 1));

                dl.AddCircleFilled(new Vector2(s.X, s.Y), 4, color);
                if (_showLineToEnemy)
                    dl.AddLine(center, new Vector2(s.X, s.Y), color, 1.0f);
                if (_showBoxESP)
                {
                    float w = 40, h = 70;
                    dl.AddRect(new Vector2(s.X - w / 2, s.Y - h / 2),
                               new Vector2(s.X + w / 2, s.Y + h / 2), color, 2f);
                }
                if (_showDistance)
                {
                    string text = $"{e.TypeTag} ({e.Position.X:F0},{e.Position.Y:F0},{e.Position.Z:F0})";
                    ImGui.GetForegroundDrawList().AddText(new Vector2(s.X + 6, s.Y), color, text);
                }
            }

            // 
            // Control panel
            ImGui.SetNextWindowBgAlpha(0.4f);
            ImGui.Begin("ABI Overlay", flags | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize);

            ImGui.Text($"Entities: {_entities.Count}");
            ImGui.Text($"Attached: {DmaMemory.IsAttached}");
            ImGui.Text($"Update: {_lastUpdateMs:F1} ms");
            ImGui.Separator();

            ImGui.TextColored(new Vector4(0.6f, 0.9f, 1f, 1f), "Camera");
            ImGui.Text($"Pos: {_cam.Position.X:F1}, {_cam.Position.Y:F1}, {_cam.Position.Z:F1}");
            ImGui.Text($"Rot: {_cam.Rotation.X:F1}, {_cam.Rotation.Y:F1}, {_cam.Rotation.Z:F1}");
            ImGui.Text($"AxisX: {_cam.AxisX.X:F2},{_cam.AxisX.Y:F2},{_cam.AxisX.Z:F2}");
            ImGui.Text($"AxisY: {_cam.AxisY.X:F2},{_cam.AxisY.Y:F2},{_cam.AxisY.Z:F2}");
            ImGui.Text($"AxisZ: {_cam.AxisZ.X:F2},{_cam.AxisZ.Y:F2},{_cam.AxisZ.Z:F2}");
            ImGui.Separator();

            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f), "Aimbot");
            ImGui.Checkbox("Use Makcu Movement", ref _useMakcuWhenAvailable);
            ImGui.SliderFloat("FOV (deg)", ref _aimFovDeg, 5f, 180f);
            ImGui.SliderFloat("Smooth", ref _aimSmooth, 1f, 50f);

            ImGui.Checkbox("Box ESP", ref _showBoxESP);
            ImGui.Checkbox("Line ESP", ref _showLineToEnemy);
            ImGui.Checkbox("Show Distance", ref _showDistance);

            ImGui.Separator();
            if (ImGui.Button(_running ? "Stop" : "Start"))
                if (_running) Stop(); else Start();

            ImGui.End();

            // 
            // Live entity list
            ImGui.SetNextWindowBgAlpha(0.35f);
            ImGui.Begin("ABI Entities", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);
            ImGui.TextColored(new Vector4(0.8f, 1f, 0.8f, 1f), $"Entity List ({_entities.Count})");

            if (ImGui.BeginTable("entities_tbl", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(600, 400)))
            {
                ImGui.TableSetupColumn("Ptr");
                ImGui.TableSetupColumn("Type");
                ImGui.TableSetupColumn("Visible");
                ImGui.TableSetupColumn("Mesh");
                ImGui.TableSetupColumn("Position");
                ImGui.TableHeadersRow();

                foreach (var e in snapshot)
                {
                    if (!e.TypeTag.Contains("AI_SCAV", StringComparison.OrdinalIgnoreCase) &&
                        !e.TypeTag.Contains("PMC", StringComparison.OrdinalIgnoreCase))
                        continue;

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0); ImGui.Text($"0x{e.Ptr:X}");
                    ImGui.TableSetColumnIndex(1); ImGui.Text(e.TypeTag);
                    ImGui.TableSetColumnIndex(2); ImGui.Text(e.Visible ? "Yes" : "No");
                    ImGui.TableSetColumnIndex(3); ImGui.Text($"0x{e.Mesh:X}");
                    ImGui.TableSetColumnIndex(4);
                    ImGui.Text($"({e.Position.X:F1},{e.Position.Y:F1},{e.Position.Z:F1})");
                }
                ImGui.EndTable();
            }
            ImGui.End();

            HandleAimbotFrame(center, scr.W, scr.H);
        }

        // 
        private static void UpdateWorld()
        {
            try
            {
                ulong module = DmaMemory.Base;
                if (module == 0) return;

                ulong gworld = DmaMemory.Read<ulong>(module + ABIOffsets.GWorld);
                if (gworld == 0) return;
                ulong ulevel = DmaMemory.Read<ulong>(gworld + ABIOffsets.PersistentLevel);
                ulong actorsArray = DmaMemory.Read<ulong>(ulevel + ABIOffsets.ActorsOffset);
                int actorCount = DmaMemory.Read<int>(ulevel + ABIOffsets.ActorSize);
                if (actorCount <= 0) return;

                // ©¤©¤ Camera
                ulong gameInst = DmaMemory.Read<ulong>(gworld + ABIOffsets.OwningGameInstance);
                ulong localPlayers = DmaMemory.Read<ulong>(gameInst + ABIOffsets.LocalPlayers);
                ulong localPlayer = DmaMemory.Read<ulong>(localPlayers);
                ulong playerController = DmaMemory.Read<ulong>(localPlayer + ABIOffsets.PlayerController);
                ulong camMgr = DmaMemory.Read<ulong>(playerController + ABIOffsets.CameraManager);
                if (camMgr == 0) return;

                ulong camCache = camMgr + ABIOffsets.CameraCachePrivate;
                Vector3 camPos = DmaMemory.Read<Vector3>(camCache + ABIOffsets.CameraPOVLocation);
                Vector3 camRot = DmaMemory.Read<Vector3>(camCache + ABIOffsets.CameraPOVRotation);

                ABIMath.GetAxes(camRot, out var axisX, out var axisY, out var axisZ);
                _cam = new CameraInfo
                {
                    Position = camPos,
                    Rotation = camRot,
                    AxisX = axisX,
                    AxisY = axisY,
                    AxisZ = axisZ,
                    Fov = 90f
                };

                var tmp = new List<ABIEntity>(actorCount);

                // ©¤©¤ Scatter actor data
                using var map = DmaMemory.Scatter();
                var round = map.AddRound(useCache: false);

                var actorPtrs = new ulong[actorCount];
                for (int i = 0; i < actorCount; i++)
                {
                    actorPtrs[i] = DmaMemory.Read<ulong>(actorsArray + (ulong)(i * 8));
                    if (actorPtrs[i] == 0) continue;

                    var idx = i;
                    round[idx].AddValueEntry<ulong>(0, actorPtrs[i] + ABIOffsets.Mesh);
                    round[idx].AddValueEntry<ulong>(1, actorPtrs[i] + ABIOffsets.RootComponent);
                    round[idx].AddValueEntry<sbyte>(2, actorPtrs[i] + ABIOffsets.CachedCharacterType);

                    round[idx].Completed += (_, cb) =>
                    {
                        if (!cb.TryGetValue(0, out ulong mesh) || mesh == 0) return;
                        if (!cb.TryGetValue(1, out ulong root) || root == 0) return;

                        var pos = DmaMemory.Read<Vector3>(root + 0x220);
                        if (pos == Vector3.Zero) return;

                        bool vis = false;
                        try
                        {
                            float sub = DmaMemory.Read<float>(mesh + ABIOffsets.LastSubmitTime);
                            float ren = DmaMemory.Read<float>(mesh + ABIOffsets.LastRenderTime);
                            vis = ren + 0.06f >= sub;
                        }
                        catch { }

                        string tag = "[?]";
                        if (cb.TryGetValue(2, out sbyte t))
                            tag = ABICharacterResolver.GetTypeName((ECharacterType)t);

                        tmp.Add(new ABIEntity
                        {
                            Ptr = actorPtrs[idx],
                            Mesh = mesh,
                            Position = pos,
                            Visible = vis,
                            TypeTag = tag
                        });
                    };
                }

                map.Execute();

                lock (_entities)
                {
                    _entities.Clear();
                    _entities.AddRange(tmp);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ABI] Scatter UpdateWorld error: {ex.Message}");
            }
        }

        // 
        private static void HandleAimbotFrame(Vector2 center, float screenW, float screenH)
        {
            List<ABIEntity> snapshot;
            lock (_entities)
                snapshot = _entities.Count > 0 ? new List<ABIEntity>(_entities) : new List<ABIEntity>();

            if (snapshot.Count == 0) return;

            ulong target = 0;
            float bestDist = float.MaxValue;
            float pixelRadius = _aimFovDeg;

            if (_cam.Fov > 0)
            {
                float ratio = (float)(Math.Tan(_aimFovDeg * Math.PI / 360.0) / Math.Tan(_cam.Fov * Math.PI / 360.0));
                pixelRadius = Math.Clamp(MathF.Abs(ratio) * (screenW / 2f), 8f, Math.Max(screenW, screenH));
            }

            foreach (var e in snapshot)
            {
                if (!ABIMath.WorldToScreen(e.Position, out var s, _cam, screenW, screenH)) continue;
                float dx = s.X - center.X, dy = s.Y - center.Y, dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= pixelRadius && dist < bestDist)
                {
                    bestDist = dist;
                    target = e.Ptr;
                }
            }

            if (target == 0 || !Input.InputManager.IsKeyDown(_aimKeyVk)) return;

            try
            {
                ulong mesh = DmaMemory.Read<ulong>(target + ABIOffsets.Mesh);
                if (mesh == 0) return;
                Vector3 neck = GetBoneWorldPositionFallback(mesh, target, 15);
                if (!ABIMath.WorldToScreen(neck, out var t, _cam, screenW, screenH)) return;

                float dx = t.X - center.X, dy = t.Y - center.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float moveFactor = MathF.Pow(MathF.Min(1f, dist / pixelRadius), 0.6f) / MathF.Max(0.001f, _aimSmooth);
                int moveX = (int)(dx * moveFactor);
                int moveY = (int)(dy * moveFactor);

                if (Device.connected && _useMakcuWhenAvailable)
                    Device.move(moveX, moveY);
                else
                    mouse_event(MOUSEEVENTF_MOVE, moveX, moveY, 0, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ABI] AimbotFrame error: {ex.Message}");
            }
        }

        // 
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

    // 
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
