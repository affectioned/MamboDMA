using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using ImGuiNET;
using MamboDMA.Services;
using VmmSharpEx.Scatter.V2;
using static MamboDMA.OverlayWindow;
using static MamboDMA.Misc;

namespace MamboDMA.Games.ABI
{
    public sealed class ABIGame : IGame
    {
        public string Name => "ArenaBreakoutInfinite";

        private bool _initialized;
        private bool _running;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ UI config ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static bool _drawBoxes = true;
        private static bool _drawNames = true;
        private static bool _drawDistance = true;
        private static bool _drawSkeletons = false;
        private static bool _showDebug = false;
        private static float _maxDistance = 800f; // meters
        private static Vector4 _colorPlayer = new(1f, 0.25f, 0.25f, 1f);
        private static Vector4 _colorBot = new(0f, 0.6f, 1f, 1f);

        // Process name for quick attach
        private const string _abiExe = "UAGame.exe";

        public void Initialize()
        {
            if (_initialized) return;
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);
            _initialized = true;
        }

        // Non-blocking attach via VmmService so AppSnapshot updates and GameSelector can reflect it.
        public void Attach()
        {
            VmmService.Attach(_abiExe);
        }

        public void Dispose()
        {
            Stop();
            DmaMemory.Dispose();
        }

        public void Start()
        {
            if (_running) return;

            // SAFETY: never start cache threads unless we are attached
            if (!DmaMemory.IsAttached)
                return;

            _running = true;

            TimerResolution.Enable1ms();  // keep loops under 15.6ms granularity
            ABICache.Start();

            Logger.Info("[ABI] cache threads started");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            ABICache.Stop();
            TimerResolution.Disable1ms();

            Logger.Info("[ABI] cache threads stopped");
        }

        public void Tick() { }

        public void Draw(ImGuiWindowFlags flags)
        {
            ImGui.Begin("Arena Breakout Infinite", flags | ImGuiWindowFlags.AlwaysAutoResize);

            bool vmmReady = DmaMemory.IsVmmReady;
            bool attached = DmaMemory.IsAttached;

            // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
            // Quick VMM Setup (no process list)
            ImGui.TextDisabled("Quick Setup");
            if (!vmmReady)
            {
                if (ImGui.Button("Init VMM"))
                {
                    VmmService.InitOnly(); // Snapshots.VmmReady=true on success
                }
                ImGui.SameLine();
                ImGui.TextDisabled("¡û initialize before attaching");
            }
            else if (!attached)
            {
                if (ImGui.Button($"Attach ({_abiExe})"))
                {
                    Attach(); // VmmService.Attach(_abiExe)
                }
                ImGui.SameLine();
                ImGui.TextDisabled("¡û attaches without process picker");
            }

            // Status chip: cache threads + attach state
            var statusCol = (attached && _running) ? new Vector4(0, 0.8f, 0, 1) :
                            attached ? new Vector4(0.85f, 0.75f, 0.15f, 1) :
                                       new Vector4(1, 0.3f, 0.2f, 1);
            DrawStatusInline(statusCol,
                attached ? (_running ? "Attached ¡¤ Threads running" : "Attached ¡¤ Threads stopped")
                         : "Not attached");

            ImGui.Separator();
            ImGui.Checkbox("Draw Boxes", ref _drawBoxes);
            ImGui.Checkbox("Draw Names", ref _drawNames);
            ImGui.Checkbox("Draw Distance", ref _drawDistance);
            //ImGui.Checkbox("Draw Skeletons", ref _drawSkeletons);
            ImGui.Checkbox("Show Debug Info", ref _showDebug);
            ImGui.SliderFloat("Max Draw Distance (m)", ref _maxDistance, 50f, 3000f);

            ImGui.Separator();
            ImGui.Text("Colors");
            ImGui.ColorEdit4("Player", ref _colorPlayer);
            ImGui.ColorEdit4("Bot", ref _colorBot);

            ImGui.Separator();

            // Start/Stop guarded by attach state
            if (!attached) ImGui.BeginDisabled();
            if (ImGui.Button(_running ? "Stop Threads" : "Start Threads"))
            {
                if (_running) Stop();
                else Start(); // guarded by IsAttached
            }
            if (!attached) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Dispose VMM"))
            {
                Dispose(); // stops threads + disposes DMA/VMM
            }

            // If not attached, don¡¯t try to render or reference DMA-driven caches
            if (!attached)
            {
                ImGui.End();
                return;
            }

            if (_showDebug)
            {
                ImGui.Separator();
                ImGui.Text("©¤ Debug Info ©¤");
                ImGui.Text($"UWorld: 0x{ABICache.UWorld:X}");
                ImGui.Text($"UGameInstance: 0x{ABICache.UGameInstance:X}");
                ImGui.Text($"GameState: 0x{ABICache.GameState:X}");
                ImGui.Text($"PersistentLevel: 0x{ABICache.PersistentLevel:X}");
                ImGui.Text($"ActorCount: {ABICache.ActorCount}");
                ImGui.Text($"ActorList.Count: {ABICache.ActorList.Count}");
                ImGui.Text($"LocalPawn: 0x{ABICache.LocalPawn:X}");
                ImGui.Text($"LocalRoot: 0x{ABICache.LocalRoot:X}");
                ImGui.Text($"CameraMgr: 0x{ABICache.LocalCameraMgr:X}");
                ImGui.Text($"CameraFov: {ABICache.Camera.Fov:F1}");
                ImGui.Text($"LocalPos: {ABICache.LocalPosition}");

                var sd = Skeleton.LastDebug;
                if (sd.Mesh != 0)
                {
                    ImGui.Separator();
                    ImGui.Text("©¤ Skeleton Debug (last actor) ©¤");
                    ImGui.Text($"Mesh: 0x{sd.Mesh:X}");
                    ImGui.Text($"BoneArray: 0x{sd.BonesData:X}  Count: {sd.BonesCount}");
                    ImGui.Text($"MaxRequestedIdx: {sd.MaxRequestedIndex}  MissingBoneIndex: {sd.MissingBoneIndex}");
                    ImGui.Text($"Note: {sd.Note ?? "(ok)"}");

                    ImGui.Text($"CTW Source: {sd.CTWSource}");
                    ImGui.Text("CTW Used:");
                    _PrintTransform(sd.ComponentToWorld_Used);
                    ImGui.Text("CTW Root:");
                    _PrintTransform(sd.ComponentToWorld_Root);
                    ImGui.Text("CTW Mesh:");
                    _PrintTransform(sd.ComponentToWorld_Mesh);

                    ImGui.Text("Samples (first few bones):");
                    for (int i = 0; i < sd.SampleCount; i++)
                    {
                        var idx = sd.SampleIndices[i];
                        var cs = sd.SampleComp[i];
                        var ws = sd.SampleWorld[i];
                        ImGui.Text($"  bone {_BoneName(idx)} [{idx}]  CS:{cs}  WS:{ws}");
                    }
                }
            }

            ImGui.End();

            // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
            // ESP overlay ¡ª only when attached & threads running & we have actors
            // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
            if (_running)
            {
                if (ABICache.ActorList.Count > 0)
                {
                    ABIESP.Render(_drawBoxes, _drawNames, _drawDistance, _drawSkeletons,
                                  _maxDistance, _colorPlayer, _colorBot);
                }
                else if (_showDebug)
                {
                    var dl = ImGui.GetForegroundDrawList();
                    var io = ImGui.GetIO();
                    const string msg = "No actors found (check offsets or filter)";
                    var size = ImGui.CalcTextSize(msg);
                    dl.AddText(new Vector2(io.DisplaySize.X / 2 - size.X / 2, 60),
                        ImGui.GetColorU32(new Vector4(1, 0.8f, 0, 1)), msg);
                }
            }
        }

        private static void _PrintTransform(in FTransform t)
        {
            ImGui.Text($"  T: {t.Translation}");
            ImGui.Text($"  S: {t.Scale3D}");
            ImGui.Text($"  Q: ({t.Rotation.X:F2},{t.Rotation.Y:F2},{t.Rotation.Z:F2},{t.Rotation.W:F2})");
        }

        private static string _BoneName(int idx)
        {
            return idx switch
            {
                1 => "Pelvis",
                12 => "Spine_01",
                13 => "Spine_02",
                14 => "Spine_03",
                15 => "Neck",
                16 => "Head",
                2 => "Thigh_L",
                4 => "Calf_L",
                5 => "Foot_L",
                7 => "Thigh_R",
                9 => "Calf_R",
                10 => "Foot_R",
                50 => "Clavicle_L",
                51 => "UpperArm_L",
                52 => "LowerArm_L",
                54 => "Hand_L",
                20 => "Clavicle_R",
                21 => "UpperArm_R",
                22 => "LowerArm_R",
                24 => "Hand_R",
                _ => "?"
            };
        }

        private static void DrawStatusInline(Vector4 color, string caption)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
            dl.AddCircleFilled(new Vector2(p.X + 5, y), 5, ImGui.ColorConvertFloat4ToU32(color));
            ImGui.Dummy(new Vector2(14, ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.TextDisabled(caption);
        }
    }

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // Core Cache with camera seqlock + skeleton cache
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    public static class ABICache
    {
        public static ulong UWorld, UGameInstance, GameState, PersistentLevel;
        public static ulong ActorArray; public static int ActorCount;
        public static ulong LocalPlayers, PlayerController, PlayerArray; public static int PlayerCount;
        public static ulong LocalPawn, LocalRoot, LocalState, LocalCameraMgr;

        // published snapshots (renderer reads these)
        public static Vector3 LocalPosition;
        public static FMinimalViewInfo Camera;

        public static List<ABIPlayer> ActorList = new();
        public static List<ActorPos> ActorPositions = new();

        // cached skeletons per Pawn
        private static readonly Dictionary<ulong, (Vector3[] pts, long ts)> _skeletons = new();

        // sync for shared snapshots
        public static readonly object Sync = new();

        // seqlock for camera+local
        private static int _camSeq;
        private static Vector3 _camLocalBuf;
        private static FMinimalViewInfo _camBuf;

        private static bool _running;

        public static void Start()
        {
            if (_running) return;

            // **HARD GUARD**: never start without attachment (avoids DmaMemory.Read exceptions)
            if (!DmaMemory.IsAttached)
                return;

            _running = true;

            new Thread(CacheWorldLoop)       { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.World"     }.Start();
            new Thread(CacheCameraLoop)      { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Camera"    }.Start();
            new Thread(CachePlayersLoop)     { IsBackground = true, Priority = ThreadPriority.Normal,      Name = "ABI.Players"   }.Start();
            new Thread(CachePositionsLoop)   { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Positions" }.Start();
            new Thread(CacheSkeletonsLoop)   { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Skeletons" }.Start();
        }

        public static void Stop() => _running = false;

        // Exposed for renderer
        public static bool TryGetSkeleton(ulong pawn, out Vector3[] pts)
        {
            lock (Sync)
            {
                if (_skeletons.TryGetValue(pawn, out var v))
                {
                    pts = v.pts;
                    return pts != null;
                }
            }
            pts = null;
            return false;
        }

        // ¡ª¡ª¡ª world
        private static void CacheWorldLoop()
        {
            while (_running)
            {
                try { CacheWorld(); } catch { }
                HighResDelay(50);
            }
        }

        private static bool CacheWorld()
        {
            UWorld = DmaMemory.Read<ulong>(DmaMemory.Base + ABIOffsets.GWorld);
            if (UWorld == 0) return false;

            UGameInstance   = DmaMemory.Read<ulong>(UWorld + ABIOffsets.UWorld_OwningGameInstance);
            GameState       = DmaMemory.Read<ulong>(UWorld + ABIOffsets.UWorld_GameState);
            PersistentLevel = DmaMemory.Read<ulong>(UWorld + ABIOffsets.UWorld_PersistentLevel);
            ActorArray      = DmaMemory.Read<ulong>(PersistentLevel + ABIOffsets.ULevel_ActorArray);
            ActorCount      = DmaMemory.Read<int>(PersistentLevel + ABIOffsets.ULevel_ActorCount);

            LocalPlayers     = DmaMemory.Read<ulong>(DmaMemory.Read<ulong>(UGameInstance + ABIOffsets.UGameInstance_LocalPlayers));
            PlayerController = DmaMemory.Read<ulong>(LocalPlayers + ABIOffsets.UPlayer_PlayerController);
            LocalCameraMgr   = DmaMemory.Read<ulong>(PlayerController + ABIOffsets.APlayerController_PlayerCameraManager);
            LocalPawn        = DmaMemory.Read<ulong>(PlayerController + ABIOffsets.APlayerController_AcknowledgedPawn);
            LocalRoot        = DmaMemory.Read<ulong>(LocalPawn + ABIOffsets.AActor_RootComponent);
            PlayerArray      = DmaMemory.Read<ulong>(GameState + ABIOffsets.AGameStateBase_PlayerArray);
            PlayerCount      = DmaMemory.Read<int>(GameState + ABIOffsets.AGameStateBase_PlayerCount);
            return true;
        }

        // ¡ª¡ª¡ª camera+local (seqlock publish)
        private static void CacheCameraLoop()
        {
            while (_running)
            {
                try
                {
                    if (LocalCameraMgr != 0 && LocalRoot != 0)
                    {
                        using var map = DmaMemory.Scatter();
                        var r = map.AddRound(false);
                        r[0].AddValueEntry<FMinimalViewInfo>(0, LocalCameraMgr + ABIOffsets.APlayerCameraManager_CameraCachePrivate + 0x10);
                        r[0].AddValueEntry<Vector3>(1, LocalRoot + ABIOffsets.USceneComponent_RelativeLocation);
                        map.Execute();

                        if (r[0].TryGetValue(0, out FMinimalViewInfo cam) &&
                            r[0].TryGetValue(1, out Vector3 local))
                        {
                            Interlocked.Increment(ref _camSeq);
                            _camBuf = cam;
                            _camLocalBuf = local;
                            Camera = cam;
                            LocalPosition = local;
                            Interlocked.Increment(ref _camSeq);
                        }
                    }
                }
                catch { }

                HighResDelay(1);
            }
        }

        public static bool TryGetCameraSnapshot(out FMinimalViewInfo cam, out Vector3 local)
        {
            cam = default;
            local = default;
            for (int i = 0; i < 3; i++)
            {
                int s1 = Volatile.Read(ref _camSeq);
                if ((s1 & 1) != 0) continue;
                cam = _camBuf;
                local = _camLocalBuf;
                Thread.MemoryBarrier();
                int s2 = Volatile.Read(ref _camSeq);
                if (s1 == s2 && (s2 & 1) == 0) return true;
            }
            return false;
        }

        // ¡ª¡ª¡ª players list (scan)
        private static void CachePlayersLoop()
        {
            while (_running)
            {
                try { CachePlayersScatter(); } catch { }
                HighResDelay(55);
            }
        }

        private static void CachePlayersScatter()
        {
            if (ActorArray == 0 || ActorCount == 0) return;

            var tmp = new List<ABIPlayer>(ActorCount);

            using var map = DmaMemory.Scatter();
            var round = map.AddRound(false);

            for (int i = 0; i < ActorCount; i++)
                round[i].AddValueEntry<ulong>(0, ActorArray + (ulong)i * 8);

            map.Execute();

            for (int i = 0; i < ActorCount; i++)
            {
                if (!round[i].TryGetValue(0, out ulong pawn) || pawn == 0 || pawn == LocalPawn)
                    continue;

                uint id = DmaMemory.Read<uint>(pawn + 24);
                string name = ABINamePool.GetName(id);
                if (string.IsNullOrEmpty(name)) continue;
                if (!name.Contains("BP_UamCharacter") &&
                    !name.Contains("BP_UamAICharacter") &&
                    !name.Contains("BP_UamRangeCharacter_C"))
                    continue;

                tmp.Add(new ABIPlayer(pawn, 0, 0, name, name.Contains("AI")));
            }

            if (tmp.Count == 0)
            {
                lock (Sync) ActorList = tmp;
                return;
            }

            using var map2 = DmaMemory.Scatter();
            var r2 = map2.AddRound(false);
            for (int i = 0; i < tmp.Count; i++)
            {
                r2[i].AddValueEntry<ulong>(0, tmp[i].Pawn + ABIOffsets.ACharacter_Mesh);
                r2[i].AddValueEntry<ulong>(1, tmp[i].Pawn + ABIOffsets.AActor_RootComponent);
            }
            map2.Execute();

            for (int i = 0; i < tmp.Count; i++)
            {
                if (r2[i].TryGetValue(0, out ulong mesh)) tmp[i] = tmp[i] with { Mesh = mesh };
                if (r2[i].TryGetValue(1, out ulong root)) tmp[i] = tmp[i] with { Root = root };
            }

            lock (Sync) ActorList = tmp;
        }

        // ¡ª¡ª¡ª positions (FAST, fully scatter ¨C no per-actor DMA in loop)
        private static void CachePositionsLoop()
        {
            var posScratch = new List<ActorPos>(256);
            var ctwPtrs = new List<ulong>(256);
            var idxMap  = new List<int>(256);

            while (_running)
            {
                try
                {
                    List<ABIPlayer> actors;
                    lock (Sync) actors = ActorList.Count == 0 ? null : new List<ABIPlayer>(ActorList);
                    if (actors != null && actors.Count > 0)
                    {
                        // Round 1: read all CTW pointers
                        using (var map = DmaMemory.Scatter())
                        {
                            var round = map.AddRound(false);
                            for (int i = 0; i < actors.Count; i++)
                                if (actors[i].Mesh != 0)
                                    round[i].AddValueEntry<ulong>(0, actors[i].Mesh + ABIOffsets.USceneComponent_ComponentToWorld_Ptr);
                            map.Execute();

                            ctwPtrs.Clear();
                            idxMap.Clear();
                            ctwPtrs.Capacity = Math.Max(ctwPtrs.Capacity, actors.Count);
                            idxMap.Capacity  = Math.Max(idxMap.Capacity, actors.Count);

                            for (int i = 0; i < actors.Count; i++)
                            {
                                if (round[i].TryGetValue(0, out ulong p) && p != 0)
                                {
                                    idxMap.Add(i);
                                    ctwPtrs.Add(p);
                                }
                            }
                        }

                        // Round 2: batch-read all FTransforms for non-zero pointers
                        var ctwValues = new FTransform[ctwPtrs.Count];
                        if (ctwPtrs.Count > 0)
                        {
                            using var map2 = DmaMemory.Scatter();
                            var r2 = map2.AddRound(false);
                            for (int j = 0; j < ctwPtrs.Count; j++)
                                r2[j].AddValueEntry<FTransform>(0, ctwPtrs[j]);
                            map2.Execute();

                            for (int j = 0; j < ctwPtrs.Count; j++)
                                r2[j].TryGetValue(0, out ctwValues[j]);
                        }

                        // Build positions (fallback to RelativeLocation only if CTW invalid)
                        posScratch.Clear();
                        posScratch.Capacity = Math.Max(posScratch.Capacity, actors.Count);

                        for (int k = 0; k < idxMap.Count; k++)
                        {
                            int i = idxMap[k];
                            var ctw = ctwValues[k];

                            Vector3 pos;
                            if (float.IsFinite(ctw.Translation.X) && Math.Abs(ctw.Rotation.W) > 1e-6f)
                            {
                                pos = ctw.Translation;
                            }
                            else
                            {
                                pos = DmaMemory.Read<Vector3>(actors[i].Mesh + ABIOffsets.USceneComponent_RelativeLocation);
                            }

                            posScratch.Add(new ActorPos(actors[i].Pawn, pos));
                        }

                        lock (Sync)
                            ActorPositions = new List<ActorPos>(posScratch);
                    }
                }
                catch { }

                HighResDelay(1); // run hot; it¡¯s fully batched now
            }
        }

        // ¡ª¡ª¡ª skeletons (background)
        private static void CacheSkeletonsLoop()
        {
            const int maxPerTick = 16;
            while (_running)
            {
                try
                {
                    List<ABIPlayer> actors;
                    lock (Sync) actors = ActorList.Count == 0 ? null : new List<ABIPlayer>(ActorList);

                    if (actors != null && actors.Count > 0)
                    {
                        int processed = 0;
                        for (int i = 0; i < actors.Count && processed < maxPerTick; i++)
                        {
                            var a = actors[i];
                            if (a.Mesh == 0) continue;

                            if (Skeleton.TryGetWorldBones(a.Mesh, a.Root, out var pts, out var dbg))
                            {
                                lock (Sync)
                                {
                                    _skeletons[a.Pawn] = (pts, DateTime.UtcNow.Ticks);
                                }
                                Skeleton.LastDebug = dbg;
                            }
                            processed++;
                        }

                        // prune old
                        lock (Sync)
                        {
                            if (_skeletons.Count > 0)
                            {
                                var now = DateTime.UtcNow.Ticks;
                                long stale = TimeSpan.FromMilliseconds(800).Ticks;
                                var toRemove = new List<ulong>(4);
                                foreach (var kv in _skeletons)
                                    if (now - kv.Value.ts > stale) toRemove.Add(kv.Key);
                                for (int k = 0; k < toRemove.Count; k++) _skeletons.Remove(toRemove[k]);
                            }
                        }
                    }
                }
                catch { }

                HighResDelay(3);
            }
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Structs
        public readonly record struct ABIPlayer(ulong Pawn, ulong Mesh, ulong Root, string Name, bool IsBot);
        public readonly record struct ActorPos(ulong Pawn, Vector3 Position);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Rotator { public float Pitch, Yaw, Roll; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct FMinimalViewInfo
        {
            public Vector3 Location;
            public Rotator Rotation;
            public float Fov;
            public float ShadowFov;
            public float DesiredFov;
            public float OrthoWidth;
        }

        private static void HighResDelay(int targetMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int sleepMs = Math.Max(0, targetMs - 1);
            if (sleepMs > 0) Thread.Sleep(sleepMs);
            while (sw.ElapsedMilliseconds < targetMs) Thread.SpinWait(80);
        }
    }

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // ESP (boxes/names/dist + skeletons)
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    public static class ABIESP
    {
        public static void Render(bool drawBoxes, bool drawNames, bool drawDistance, bool drawSkeletons,
            float maxDistMeters, Vector4 colorPlayer, Vector4 colorBot)
        {
            if (!ABICache.TryGetCameraSnapshot(out var cam, out var local)) return;

            List<ABICache.ABIPlayer> actors;
            List<ABICache.ActorPos> positions;
            lock (ABICache.Sync)
            {
                if (ABICache.ActorList.Count == 0 || ABICache.ActorPositions.Count == 0) return;
                actors = new List<ABICache.ABIPlayer>(ABICache.ActorList);
                positions = new List<ABICache.ActorPos>(ABICache.ActorPositions);
            }

            var posMap = new Dictionary<ulong, Vector3>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
                posMap[positions[i].Pawn] = positions[i].Position;

            var list = ImGui.GetForegroundDrawList();
            var io = ImGui.GetIO();
            float scrW = io.DisplaySize.X, scrH = io.DisplaySize.Y;

            for (int i = 0; i < actors.Count; i++)
            {
                if (!posMap.TryGetValue(actors[i].Pawn, out var pos)) continue;

                float distCm = Vector3.Distance(local, pos);
                float distM = distCm / 100f;
                if (distM > maxDistMeters) continue;

                if (!ABIMath.WorldToScreen(pos, cam, scrW, scrH, out var screen)) continue;

                uint clr = ImGui.GetColorU32(actors[i].IsBot ? colorBot : colorPlayer);

                float bh = Math.Clamp(150f / MathF.Max(distM, 3f), 60f, 250f);
                float bw = bh * 0.35f;

                Vector2 min = new(screen.X - bw / 2, screen.Y - bh / 2);
                Vector2 max = new(screen.X + bw / 2, screen.Y + bh / 2);

                if (drawBoxes) DrawBox(list, min, max, clr, 1.5f);
                if (drawNames) list.AddText(new Vector2(screen.X - 18, min.Y - 18), clr, actors[i].IsBot ? "BOT" : "PMC");
                if (drawDistance) list.AddText(new Vector2(screen.X - 12, max.Y + 5), 0xFFFFFFFF, $"{distM:F1} m");

                if (drawSkeletons && actors[i].Mesh != 0)
                {
                    if (ABICache.TryGetSkeleton(actors[i].Pawn, out var wp) && wp != null)
                        Skeleton.Draw(list, wp, cam, scrW, scrH, clr);
                }
            }
        }

        private static void DrawBox(ImDrawListPtr list, Vector2 min, Vector2 max, uint color, float t)
        {
            float w = max.X - min.X;
            float h = max.Y - min.Y;
            float c = MathF.Min(20, MathF.Min(w * 0.25f, h * 0.25f));
            list.AddLine(min, new(min.X + c, min.Y), color, t);
            list.AddLine(min, new(min.X, min.Y + c), color, t);
            list.AddLine(new(max.X - c, min.Y), new(max.X, min.Y), color, t);
            list.AddLine(new(max.X, min.Y), new(max.X, min.Y + c), color, t);
            list.AddLine(new(min.X, max.Y - c), new(min.X, max.Y), color, t);
            list.AddLine(new(min.X, max.Y), new(min.X + c, max.Y), color, t);
            list.AddLine(new(max.X - c, max.Y), max, color, t);
            list.AddLine(max, new(max.X, max.Y - c), color, t);
        }
    }

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // Skeleton helper (14-bone minimal) + DEBUG
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    internal static class Skeleton
    {
        private const int Pelvis = 1;
        private const int Spine_01 = 12, Spine_02 = 13, Spine_03 = 14, Neck = 15, Head = 16;
        private const int Thigh_L = 2, Calf_L = 4, Foot_L = 5;
        private const int Thigh_R = 7, Calf_R = 9, Foot_R = 10;
        private const int Clavicle_L = 50, UpperArm_L = 51, LowerArm_L = 52, Hand_L = 54;
        private const int Clavicle_R = 20, UpperArm_R = 21, LowerArm_R = 22, Hand_R = 24;

        private static readonly int[] _fetch = new int[]
        {
            Pelvis, Spine_01, Spine_03, Head,
            Clavicle_L, UpperArm_L, Hand_L,
            Clavicle_R, UpperArm_R, Hand_R,
            Thigh_L, Foot_L, Thigh_R, Foot_R
        };

        public const int IDX_Pelvis = 0;
        public const int IDX_Spine_01 = 1, IDX_Spine_03 = 2, IDX_Head = 3;
        public const int IDX_Clavicle_L = 4, IDX_UpperArm_L = 5, IDX_Hand_L = 6;
        public const int IDX_Clavicle_R = 7, IDX_UpperArm_R = 8, IDX_Hand_R = 9;
        public const int IDX_Thigh_L = 10, IDX_Foot_L = 11;
        public const int IDX_Thigh_R = 12, IDX_Foot_R = 13;

        public struct DebugInfo
        {
            public ulong Mesh;
            public ulong BonesData;
            public int BonesCount;
            public int MaxRequestedIndex;
            public int MissingBoneIndex;
            public string Note;

            public string CTWSource;
            public FTransform ComponentToWorld_Used;
            public FTransform ComponentToWorld_Root;
            public FTransform ComponentToWorld_Mesh;

            public int SampleCount;
            public int[] SampleIndices;
            public Vector3[] SampleComp;
            public Vector3[] SampleWorld;

            public Vector3 HeadWorld;
            public bool W2SHeadOK;
            public Vector2 HeadScreen;
        }
        public static DebugInfo LastDebug;

        private static bool IsSane(in FTransform t)
        {
            bool finite =
                float.IsFinite(t.Scale3D.X) && float.IsFinite(t.Scale3D.Y) && float.IsFinite(t.Scale3D.Z) &&
                float.IsFinite(t.Translation.X) && float.IsFinite(t.Translation.Y) && float.IsFinite(t.Translation.Z) &&
                float.IsFinite(t.Rotation.W);
            bool nonZeroScale = Math.Abs(t.Scale3D.X) > 1e-4f || Math.Abs(t.Scale3D.Y) > 1e-4f || Math.Abs(t.Scale3D.Z) > 1e-4f;
            bool plausibleT   = Math.Abs(t.Translation.X) < 5e6f && Math.Abs(t.Translation.Y) < 5e6f && Math.Abs(t.Translation.Z) < 5e6f;
            return finite && nonZeroScale && plausibleT;
        }

        public static bool TryGetWorldBones(ulong mesh, ulong root, out Vector3[] worldPoints, out DebugInfo dbg)
        {
            dbg = default;
            dbg.Mesh = mesh;
            worldPoints = null;

            try
            {
                using var map = DmaMemory.Scatter();
                var r = map.AddRound(false);

                r[0].AddValueEntry<ulong>(0, root + ABIOffsets.USceneComponent_ComponentToWorld_Ptr);
                r[0].AddValueEntry<ulong>(1, mesh + ABIOffsets.USceneComponent_ComponentToWorld_Ptr);

                r[0].AddValueEntry<Vector3>(4, mesh + ABIOffsets.USceneComponent_RelativeLocation);
                r[0].AddValueEntry<ABICache.Rotator>(5, mesh + 0x0178);
                r[0].AddValueEntry<Vector3>(6, mesh + 0x0184);

                ulong arr = mesh + ABIOffsets.USkeletalMeshComponent_CachedComponentSpaceTransforms;
                r[0].AddValueEntry<ulong>(2, arr + 0x0);
                r[0].AddValueEntry<int>  (3, arr + 0x8);

                map.Execute();

                FTransform ctwRoot = default, ctwMesh = default;
                if (r[0].TryGetValue(0, out ulong rootPtr) && rootPtr != 0UL) ctwRoot = DmaMemory.Read<FTransform>(rootPtr);
                if (r[0].TryGetValue(1, out ulong meshPtr) && meshPtr != 0UL) ctwMesh = DmaMemory.Read<FTransform>(meshPtr);

                FTransform ctw = IsSane(ctwMesh) ? ctwMesh : default;

                if (!IsSane(ctw) && IsSane(ctwRoot) &&
                    r[0].TryGetValue(4, out Vector3 relT) &&
                    r[0].TryGetValue(5, out ABICache.Rotator relR) &&
                    r[0].TryGetValue(6, out Vector3 relS))
                {
                    var relQ = MakeQuatFromRotator(relR);
                    var meshRel = new FTransform { Translation = relT, Rotation = relQ, Scale3D = relS };
                    ctw = Mul(ctwRoot, meshRel);
                    dbg.CTWSource = "root(ptr)*meshRelative";
                }
                else
                {
                    dbg.CTWSource = "mesh(ptr)";
                }

                if (!IsSane(ctw)) { dbg.Note = "CTW invalid"; return false; }

                dbg.ComponentToWorld_Root = ctwRoot;
                dbg.ComponentToWorld_Mesh = ctwMesh;
                dbg.ComponentToWorld_Used = ctw;

                if (!r[0].TryGetValue(2, out ulong data) || data == 0 ||
                    !r[0].TryGetValue(3, out int count) || count <= 0)
                { dbg.Note = "Bones header invalid"; return false; }

                using var map2 = DmaMemory.Scatter();
                var r2 = map2.AddRound(false);
                const int SZ = 0x30;
                for (int i = 0; i < _fetch.Length; i++)
                    r2[i].AddValueEntry<FTransform>(0, data + (ulong)(_fetch[i] * SZ));
                map2.Execute();

                const int SAMPLE = 6;
                dbg.SampleCount = Math.Min(SAMPLE, _fetch.Length);
                dbg.SampleIndices = new int[dbg.SampleCount];
                dbg.SampleComp = new Vector3[dbg.SampleCount];
                dbg.SampleWorld = new Vector3[dbg.SampleCount];

                worldPoints = new Vector3[_fetch.Length];
                for (int i = 0; i < _fetch.Length; i++)
                {
                    if (!r2[i].TryGetValue(0, out FTransform boneCS))
                    { dbg.MissingBoneIndex = _fetch[i]; dbg.Note = "Bone read failed"; return false; }

                    var ws = TransformPosition(ctw, boneCS.Translation);
                    worldPoints[i] = ws;

                    if (i < dbg.SampleCount)
                    {
                        dbg.SampleIndices[i] = _fetch[i];
                        dbg.SampleComp[i] = boneCS.Translation;
                        dbg.SampleWorld[i] = ws;
                    }
                }

                dbg.Note = "ok";
                return true;
            }
            catch (Exception ex)
            {
                dbg.Note = $"Exception: {ex.Message}";
                return false;
            }
        }

        public static void Draw(ImDrawListPtr list, Vector3[] wp, ABICache.FMinimalViewInfo cam, float w, float h, uint color)
        {
            void seg(int a, int b)
            {
                if (ABIMath.WorldToScreen(wp[a], cam, w, h, out var A) &&
                    ABIMath.WorldToScreen(wp[b], cam, w, h, out var B))
                {
                    list.AddLine(A, B, color, 1.5f);
                }
            }

            seg(IDX_Pelvis, IDX_Spine_01);
            seg(IDX_Spine_01, IDX_Spine_03);
            seg(IDX_Spine_03, IDX_Head);

            seg(IDX_Spine_03, IDX_Clavicle_L);
            seg(IDX_Clavicle_L, IDX_UpperArm_L);
            seg(IDX_UpperArm_L, IDX_Hand_L);

            seg(IDX_Spine_03, IDX_Clavicle_R);
            seg(IDX_Clavicle_R, IDX_UpperArm_R);
            seg(IDX_UpperArm_R, IDX_Hand_R);

            seg(IDX_Pelvis, IDX_Thigh_L);
            seg(IDX_Thigh_L, IDX_Foot_L);
            seg(IDX_Pelvis, IDX_Thigh_R);
            seg(IDX_Thigh_R, IDX_Foot_R);
        }

        private static Vector3 TransformPosition(in FTransform t, in Vector3 p)
        {
            var scaled  = new Vector3(p.X * t.Scale3D.X, p.Y * t.Scale3D.Y, p.Z * t.Scale3D.Z);
            var rotated = RotateVector(t.Rotation, scaled);
            return rotated + t.Translation;
        }

        private static Vector3 RotateVector(in FQuat q, in Vector3 v)
        {
            var qv = new Vector3(q.X, q.Y, q.Z);
            var t = 2f * Vector3.Cross(qv, v);
            return v + q.W * t + Vector3.Cross(qv, t);
        }

        private static FQuat MakeQuatFromRotator(ABICache.Rotator r)
        {
            float pitch = r.Pitch * (float)(Math.PI / 180.0);
            float yaw   = r.Yaw   * (float)(Math.PI / 180.0);
            float roll  = r.Roll  * (float)(Math.PI / 180.0);

            float cy = MathF.Cos(yaw * 0.5f),  sy = MathF.Sin(yaw * 0.5f);
            float cp = MathF.Cos(pitch * 0.5f),sp = MathF.Sin(pitch * 0.5f);
            float cr = MathF.Cos(roll * 0.5f), sr = MathF.Sin(roll * 0.5f);

            return new FQuat {
                W = cr*cp*cy + sr*sp*sy,
                X = sr*cp*cy - cr*sp*sy,
                Y = cr*sp*cy + sr*cp*sy,
                Z = cr*cp*sy - sr*sp*cy
            };
        }

        private static FQuat Mul(in FQuat a, in FQuat b) => new FQuat {
            W = a.W*b.W - a.X*b.X - a.Y*b.Y - a.Z*b.Z,
            X = a.W*b.X + a.X*b.W + a.Y*b.Z - a.Z*b.Y,
            Y = a.W*b.Y - a.X*b.Z + a.Y*b.W + a.Z*b.X,
            Z = a.W*b.Z + a.X*b.Y - a.Y*b.X + a.Z*b.W
        };

        private static FTransform Mul(in FTransform a, in FTransform b)
        {
            var rot = Mul(a.Rotation, b.Rotation);
            var scl = new Vector3(a.Scale3D.X * b.Scale3D.X, a.Scale3D.Y * b.Scale3D.Y, a.Scale3D.Z * b.Scale3D.Z);
            var t   = a.Translation + RotateVector(a.Rotation, new Vector3(b.Translation.X * a.Scale3D.X,
                                                                           b.Translation.Y * a.Scale3D.Y,
                                                                           b.Translation.Z * a.Scale3D.Z));
            return new FTransform { Rotation = rot, Scale3D = scl, Translation = t };
        }
    }

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // Name Pool (unchanged)
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    public static class ABINamePool
    {
        private static readonly ulong GNames = DmaMemory.Base + ABIOffsets.GNames;
        private static byte _xorKey;

        public static string GetName(uint key)
        {
            try
            {
                if (_xorKey == 0)
                    _xorKey = DmaMemory.Read<byte>(DmaMemory.Base + ABIOffsets.DecryuptKey);

                uint chunk = key >> 16;
                ushort offset = (ushort)key;
                ulong poolChunk = DmaMemory.Read<ulong>(GNames + ((ulong)(chunk + 2) * 8));
                ulong entry = poolChunk + (ulong)(2 * offset);
                short header = DmaMemory.Read<short>(entry);
                int len = header >> 6;
                if (len <= 0 || len > 512) return string.Empty;

                byte[] buf = DmaMemory.ReadBytes(entry + 2, (uint)len);
                FNameDecrypt(buf, len);
                return Encoding.ASCII.GetString(buf);
            }
            catch { return string.Empty; }
        }

        private static void FNameDecrypt(byte[] input, int nameLength)
        {
            if (input == null || nameLength <= 0) return;
            if (nameLength > input.Length) nameLength = input.Length;

            byte key = _xorKey;

            for (int i = 0; i < nameLength; ++i)
            {
                byte dl = (byte)(((key >> 5) & 0x02) ^ key);
                byte cl = (byte)((((byte)(dl & 0x02) << 5)) ^ dl);
                byte al = (byte)((((cl >> 5) & 0x02) ^ input[i] ^ cl) ^ 0x39);
                input[i] = al;
            }
        }
    }

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // System timer resolution helper
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    internal static class TimerResolution
    {
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uMilliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uMilliseconds);
        private static int _ref;

        public static void Enable1ms()
        {
            if (Interlocked.Increment(ref _ref) == 1) timeBeginPeriod(1);
        }

        public static void Disable1ms()
        {
            if (Interlocked.Decrement(ref _ref) == 0) timeEndPeriod(1);
        }
    }
}
