// MamboDMA/Games/ABI/Players.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using MamboDMA.Services;
using VmmSharpEx.Scatter.V2;

namespace MamboDMA.Games.ABI
{
    internal static class ABIOffsetsExt
    {
        public const int OFF_PAWN_ASC            = 0x15E0;
        public const int OFF_PAWN_DEATHCOMP      = 0x1728;
        public const int OFF_ASC_ATTRSETS        = 0x0188;
        public const int OFF_ATTR_HEALTH         = 0x48;
        public const int OFF_ATTR_HEALTHMAX      = 0x4C;
        public const int OFF_DEATHCOMP_DEATHINFO = 0x0240;

        // visibility timestamps (optional)
        public const ulong OFF_MESH_TIMERS = 0x3D8;
        public const int   OFF_LASTSUBMIT  = 0x4;
        public const int   OFF_LASTONSCREEN= 0xC;

        // optional
        public const int OFF_CHAR_TICKING_ON_DEATH = 0x16B0;

        public const float VIS_TICK = 0.06f;
    }

    public static class Players
    {
        // ©¤©¤ world/camera pointers
        public static ulong UWorld, UGameInstance, GameState, PersistentLevel;
        public static ulong ActorArray; public static int ActorCount;
        public static ulong LocalPlayers, PlayerController, PlayerArray; public static int PlayerCount;
        public static ulong LocalPawn, LocalRoot, LocalState, LocalCameraMgr;

        // published snapshots
        public static Vector3 LocalPosition;     // world (bias applied)
        public static FMinimalViewInfo Camera;   // for W2S
        public static float CtrlYaw;             // from APlayerController::ControlRotation

        // origin bias (kept but ABI usually doesn¡¯t shift origin)
        private static Vector3 _originBias;
        private static Vector3 _prevLocalWorld;
        private static bool _havePrevLocal;

        // public views
        public static List<ABIPlayer> ActorList = new();
        public static List<ActorPos>  ActorPositions = new();

        // skeleton cache
        private static readonly Dictionary<ulong, (Vector3[] pts, long ts)> _skeletons = new();

        public static readonly object Sync = new();

        // seqlock for camera/local
        private static int _camSeq;
        private static Vector3 _camLocalBuf;
        private static FMinimalViewInfo _camBuf;
        private static float _ctrlYawBuf;
        private static Vector3 _camBiasBuf;

        // coherent frame for ESP
        public struct Frame
        {
            public FMinimalViewInfo Cam;
            public Vector3 Local;
            public List<ActorPos> Positions;
            public long Stamp;
        }
        private static int _frameSeq;
        private static Frame _frameBuf;

        private static bool _running;

        // stable cache (sticky)
        private struct ActorCacheEntry
        {
            public ulong Pawn;
            public ulong Mesh;
            public ulong Root;
            public ulong ASC;
            public ulong DeathComp;
            public ulong HealthSet;
            public bool  IsBot;
            public string Name;
        }
        private static readonly Dictionary<ulong, ActorCacheEntry> _actorCache = new(1024);

        // throttles
        private static long _lastEnumTicks;
        private const int ENUM_PERIOD_MS = 150;

        // API
        public static void StartCache()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            new Thread(CacheWorldLoop)     { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.World"     }.Start();
            new Thread(CacheCameraLoop)    { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Camera"    }.Start();
            new Thread(CachePlayersLoop)   { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.Players"   }.Start();
            new Thread(CacheVitalsLoop)    { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.Vitals"    }.Start();
            new Thread(CachePositionsLoop) { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Positions" }.Start();
            new Thread(CacheSkeletonsLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.Skeletons" }.Start();
            new Thread(FramePulseLoop)     { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.FramePulse"}.Start();
        }

        public static void Stop() => _running = false;

        // seqlock helpers
        public static bool TryGetFrame(out Frame f)
        {
            f = default;
            for (int i = 0; i < 3; i++)
            {
                int s1 = Volatile.Read(ref _frameSeq);
                if ((s1 & 1) != 0) continue;
                f = _frameBuf;
                Thread.MemoryBarrier();
                int s2 = Volatile.Read(ref _frameSeq);
                if (s1 == s2 && (s2 & 1) == 0 && f.Positions != null) return true;
            }
            return false;
        }

        public static bool TryGetSkeleton(ulong pawn, out Vector3[] pts)
        {
            lock (Sync)
            {
                if (_skeletons.TryGetValue(pawn, out var v))
                { pts = v.pts; return pts != null; }
            }
            pts = null; return false;
        }

        internal static void __SetSkeletonUnsafe(ulong pawn, Vector3[] pts)
        {
            _skeletons[pawn] = (pts, System.Diagnostics.Stopwatch.GetTimestamp());
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ loops ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static void CacheWorldLoop()
        {
            while (_running) { try { CacheWorld(); } catch { } HighResDelay(50); }
        }

        private static bool CacheWorld()
        {
            UWorld = DmaMemory.Read<ulong>(DmaMemory.Base + ABIOffsets.GWorld);
            if (UWorld == 0) return false;

            UGameInstance   = DmaMemory.Read<ulong>(UWorld + ABIOffsets.UWorld_OwningGameInstance);
            GameState       = DmaMemory.Read<ulong>(UWorld + ABIOffsets.UWorld_GameState);
            PersistentLevel = DmaMemory.Read<ulong>(UWorld + ABIOffsets.UWorld_PersistentLevel);

            ActorArray      = DmaMemory.Read<ulong>(PersistentLevel + ABIOffsets.ULevel_ActorArray + 0x00);
            ActorCount      = DmaMemory.Read<int>  (PersistentLevel + ABIOffsets.ULevel_ActorArray + 0x08);

            LocalPlayers     = DmaMemory.Read<ulong>(DmaMemory.Read<ulong>(UGameInstance + ABIOffsets.UGameInstance_LocalPlayers));
            PlayerController = DmaMemory.Read<ulong>(LocalPlayers + ABIOffsets.UPlayer_PlayerController);
            LocalCameraMgr   = DmaMemory.Read<ulong>(PlayerController + ABIOffsets.APlayerController_PlayerCameraManager);
            LocalPawn        = DmaMemory.Read<ulong>(PlayerController + ABIOffsets.APlayerController_AcknowledgedPawn);
            LocalRoot        = DmaMemory.Read<ulong>(LocalPawn + ABIOffsets.AActor_RootComponent);

            PlayerArray      = DmaMemory.Read<ulong>(GameState + ABIOffsets.AGameStateBase_PlayerArray);
            PlayerCount      = DmaMemory.Read<int>  (GameState + ABIOffsets.AGameStateBase_PlayerCount);
            return true;
        }

        private static void CacheCameraLoop()
        {
            const bool useLastFrameCam = false;
            while (_running)
            {
                try
                {
                    if (LocalCameraMgr != 0 && LocalRoot != 0 && PlayerController != 0)
                    {
                        using var map = DmaMemory.Scatter();
                        var r = map.AddRound(false);

                        ulong camCache = useLastFrameCam
                            ? ABIOffsets.APlayerCameraManager_LastFrameCameraCachePrivate
                            : ABIOffsets.APlayerCameraManager_CameraCachePrivate;

                        // camera cache
                        r[0].AddValueEntry<FMinimalViewInfo>(0, LocalCameraMgr + camCache + 0x10);

                        // NOTE: for local position use Root->RelativeLocation (ABI world pos)
                        r[0].AddValueEntry<Vector3>(1, LocalRoot + ABIOffsets.USceneComponent_RelativeLocation);

                        // control yaw
                        r[0].AddValueEntry<Rotator>(2, PlayerController + ABIOffsets.AController_ControlRotation);

                        map.Execute();

                        if (r[0].TryGetValue(0, out FMinimalViewInfo cam) &&
                            r[0].TryGetValue(1, out Vector3 localWorld))
                        {
                            // origin jump guard
                            if (_havePrevLocal)
                            {
                                var jump = _prevLocalWorld - localWorld;
                                if (jump.Length() > 5000f) _originBias += jump;
                            }
                            _prevLocalWorld = localWorld;
                            _havePrevLocal = true;

                            var localBiased = localWorld + _originBias;
                            cam.Location += _originBias;

                            float ctrlYaw = r[0].TryGetValue(2, out Rotator ctrlRot) ? ctrlRot.Yaw : cam.Rotation.Yaw;

                            Interlocked.Increment(ref _camSeq);
                            _camBuf      = cam;
                            _camLocalBuf = localBiased;
                            _ctrlYawBuf  = ctrlYaw;
                            _camBiasBuf  = _originBias; // capture
                            Camera       = cam;
                            LocalPosition= localBiased;
                            CtrlYaw      = ctrlYaw;
                            Interlocked.Increment(ref _camSeq);
                        }
                    }
                }
                catch { }
                HighResDelay(1); // micro-loop keeps camera fresh
            }
        }

        private static void CachePlayersLoop()
        {
            while (_running)
            {
                try
                {
                    var now = System.Diagnostics.Stopwatch.GetTimestamp();
                    double ms   = now * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double last = _lastEnumTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (ms - last < ENUM_PERIOD_MS) { HighResDelay(10); continue; }
                    _lastEnumTicks = now;

                    if (ActorArray == 0 || ActorCount <= 0) { lock (Sync) ActorList = new(); continue; }

                    var tmp = new List<ABIPlayer>(Math.Min(ActorCount, 2048));

                    using (var map = DmaMemory.Scatter())
                    {
                        var round = map.AddRound(false);
                        int take = Math.Min(ActorCount, 2048);
                        for (int i = 0; i < take; i++)
                            round[i].AddValueEntry<ulong>(0, ActorArray + (ulong)i * 8);
                        map.Execute();

                        for (int i = 0; i < take; i++)
                        {
                            if (!round[i].TryGetValue(0, out ulong pawn) || pawn == 0 || pawn == LocalPawn) continue;

                            uint id = DmaMemory.Read<uint>(pawn + 24);
                            string name = ABINamePool.GetName(id);
                            if (string.IsNullOrEmpty(name)) continue;

                            if (!name.Contains("BP_UamCharacter") &&
                                !name.Contains("BP_UamAICharacter") &&
                                !name.Contains("BP_UamRangeCharacter_C"))
                                continue;

                            bool isBot = name.Contains("AI");
                            tmp.Add(new ABIPlayer(pawn, 0, 0, name, isBot, 0, 0, 0));
                        }
                    }

                    // resolve sticky ptrs (Mesh/Root/ASC/DeathComp) for those missing
                    if (tmp.Count > 0)
                    {
                        using var map2 = DmaMemory.Scatter();
                        var r2 = map2.AddRound(false);
                        int idx = 0;
                        var idxMap = new List<int>(tmp.Count);

                        for (int i = 0; i < tmp.Count; i++)
                        {
                            var t = tmp[i];
                            _actorCache.TryGetValue(t.Pawn, out var ac);

                            bool need = false;
                            if (ac.Mesh == 0)      { r2[idx].AddValueEntry<ulong>(0, t.Pawn + ABIOffsets.ACharacter_Mesh); need = true; }
                            if (ac.Root == 0)      { r2[idx].AddValueEntry<ulong>(1, t.Pawn + ABIOffsets.AActor_RootComponent); need = true; }
                            if (ac.ASC == 0)       { r2[idx].AddValueEntry<ulong>(2, t.Pawn + (ulong)ABIOffsetsExt.OFF_PAWN_ASC); need = true; }
                            if (ac.DeathComp == 0) { r2[idx].AddValueEntry<ulong>(3, t.Pawn + (ulong)ABIOffsetsExt.OFF_PAWN_DEATHCOMP); need = true; }
                            if (need) { idxMap.Add(i); idx++; }
                        }

                        if (idx > 0)
                        {
                            map2.Execute();
                            for (int k = 0; k < idxMap.Count; k++)
                            {
                                int i = idxMap[k];
                                var t = tmp[i];
                                _actorCache.TryGetValue(t.Pawn, out var ac);

                                if (r2[k].TryGetValue(0, out ulong mesh) && mesh != 0)      ac.Mesh = mesh;
                                if (r2[k].TryGetValue(1, out ulong root) && root != 0)      ac.Root = root;
                                if (r2[k].TryGetValue(2, out ulong asc)  && asc  != 0)      ac.ASC  = asc;
                                if (r2[k].TryGetValue(3, out ulong dc)   && dc   != 0)      ac.DeathComp = dc;

                                ac.IsBot = t.IsBot; ac.Name = t.Name;
                                _actorCache[t.Pawn] = ac;
                            }
                        }

                        // write back sticky values into list
                        for (int i = 0; i < tmp.Count; i++)
                        {
                            var t = tmp[i];
                            if (_actorCache.TryGetValue(t.Pawn, out var ac))
                                tmp[i] = new ABIPlayer(t.Pawn, ac.Mesh, ac.Root, t.Name, ac.IsBot, ac.ASC, ac.HealthSet, ac.DeathComp);
                        }
                    }

                    lock (Sync) ActorList = tmp;
                }
                catch { }
            }
        }

        private static void CacheVitalsLoop()
        {
            while (_running)
            {
                try
                {
                    List<ABIPlayer> actors;
                    lock (Sync) actors = ActorList.Count == 0 ? null : new List<ABIPlayer>(ActorList);
                    if (actors == null || actors.Count == 0) { HighResDelay(8); continue; }

                    // Resolve missing HealthSet once
                    var needHS = new List<int>(128);
                    for (int i = 0; i < actors.Count; i++)
                    {
                        var a = actors[i];
                        if (a.ASC == 0) continue;
                        if (_actorCache.TryGetValue(a.Pawn, out var ac) && ac.HealthSet != 0) continue;
                        needHS.Add(i);
                    }

                    if (needHS.Count > 0)
                    {
                        using var map = DmaMemory.Scatter();
                        var rd = map.AddRound(false);

                        for (int k = 0; k < needHS.Count; k++)
                        {
                            int i = needHS[k];
                            ulong basePtr = actors[i].ASC + (ulong)ABIOffsetsExt.OFF_ASC_ATTRSETS;
                            rd[k].AddValueEntry<ulong>(0, basePtr + 0x0); // Data
                            rd[k].AddValueEntry<int>(1,  basePtr + 0x8); // Num
                        }
                        map.Execute();

                        const int MAX_SCAN = 8;
                        for (int k = 0; k < needHS.Count; k++)
                        {
                            int i = needHS[k];
                            if (!rd[k].TryGetValue(0, out ulong data) || data == 0) continue;
                            if (!rd[k].TryGetValue(1, out int num ) || num <= 0) continue;

                            int take = Math.Min(num, MAX_SCAN);
                            using var map2 = DmaMemory.Scatter();
                            var r2 = map2.AddRound(false);
                            for (int e = 0; e < take; e++)
                                r2[e].AddValueEntry<ulong>(0, data + (ulong)e * 8);
                            map2.Execute();

                            var hsCandidates = new List<ulong>(take);
                            for (int e = 0; e < take; e++)
                            {
                                if (r2[e].TryGetValue(0, out ulong hs) && hs != 0)
                                    hsCandidates.Add(hs);
                            }

                            ulong resolved = 0;
                            if (hsCandidates.Count > 0)
                            {
                                using var map3 = DmaMemory.Scatter();
                                var r3 = map3.AddRound(false);
                                for (int e = 0; e < hsCandidates.Count; e++)
                                {
                                    ulong hs = hsCandidates[e];
                                    r3[e].AddValueEntry<float>(0, hs + (ulong)ABIOffsetsExt.OFF_ATTR_HEALTH);
                                    r3[e].AddValueEntry<float>(1, hs + (ulong)ABIOffsetsExt.OFF_ATTR_HEALTHMAX);
                                }
                                map3.Execute();

                                for (int e = 0; e < hsCandidates.Count; e++)
                                {
                                    r3[e].TryGetValue(0, out float h);
                                    r3[e].TryGetValue(1, out float hm);
                                    if (hm > 1f && hm < 5000f && h >= -50f && h < hm * 2.5f)
                                    { resolved = hsCandidates[e]; break; }
                                }
                            }

                            if (resolved != 0)
                            {
                                if (!_actorCache.TryGetValue(actors[i].Pawn, out var ac)) ac = new ActorCacheEntry { Pawn = actors[i].Pawn };
                                ac.HealthSet = resolved;
                                _actorCache[actors[i].Pawn] = ac;
                                actors[i] = new ABIPlayer(actors[i].Pawn, actors[i].Mesh, actors[i].Root, actors[i].Name, actors[i].IsBot, actors[i].ASC, resolved, actors[i].DeathComp);
                            }
                        }

                        lock (Sync) ActorList = actors;
                    }

                    // Read vitals (light)
                    var hsList = new List<(int idx, ulong hs)>(actors.Count);
                    for (int i = 0; i < actors.Count; i++)
                        if (actors[i].HealthSet != 0) hsList.Add((i, actors[i].HealthSet));
                    if (hsList.Count == 0) { HighResDelay(8); continue; }

                    using var mapH = DmaMemory.Scatter();
                    var rh = mapH.AddRound(false);
                    for (int k = 0; k < hsList.Count; k++)
                    {
                        ulong hs = hsList[k].hs;
                        rh[k].AddValueEntry<float>(0, hs + (ulong)ABIOffsetsExt.OFF_ATTR_HEALTH);
                        rh[k].AddValueEntry<float>(1, hs + (ulong)ABIOffsetsExt.OFF_ATTR_HEALTHMAX);
                    }
                    mapH.Execute();

                    var vitals = new Dictionary<ulong, (float h, float hm)>(hsList.Count);
                    for (int k = 0; k < hsList.Count; k++)
                    {
                        rh[k].TryGetValue(0, out float h);
                        rh[k].TryGetValue(1, out float hm);
                        var pawn = actors[hsList[k].idx].Pawn;
                        vitals[pawn] = (h, hm);
                    }
                    // stash into ActorPositions merge step (we merge in positions loop)
                    lock (_pendingVitalsSync) _pendingVitals = vitals;
                }
                catch { }

                HighResDelay(8);
            }
        }

        private static readonly object _pendingVitalsSync = new();
        private static Dictionary<ulong, (float h, float hm)> _pendingVitals;

        private static void CachePositionsLoop()
        {
            var posScratch = new List<ActorPos>(256);

            while (_running)
            {
                try
                {
                    List<ABIPlayer> actors;
                    Dictionary<ulong, (float h, float hm)> vitals = null;
                    lock (Sync)
                    {
                        actors = ActorList.Count == 0 ? null : new List<ABIPlayer>(ActorList);
                    }
                    lock (_pendingVitalsSync)
                    {
                        if (_pendingVitals != null)
                            vitals = new Dictionary<ulong, (float h, float hm)>(_pendingVitals);
                    }

                    if (actors != null && actors.Count > 0)
                    {
                        // Snapshot camera bias for this frame
                        TryGetCameraSnapshot(out var camRaw, out var localRaw, out _, out var camBias);
                        var frameBias = camBias;

                        // Read Root->RelativeLocation (WORLD), CTW pointer, timers/flags
                        var relLoc = new Vector3[actors.Count];
                        var ctwPtrs = new ulong[actors.Count];
                        var lastSubmit = new float[actors.Count];
                        var lastOnScr = new float[actors.Count];
                        var deadFlag = new bool[actors.Count];
                        var deadComp = new bool[actors.Count];

                        using (var mapA = DmaMemory.Scatter())
                        {
                            var r = mapA.AddRound(false);
                            for (int i = 0; i < actors.Count; i++)
                            {
                                var a = actors[i];

                                if (a.Root != 0)
                                    r[i].AddValueEntry<Vector3>(0, a.Root + ABIOffsets.USceneComponent_RelativeLocation);

                                if (a.Mesh != 0)
                                {
                                    r[i].AddValueEntry<ulong>(1, a.Mesh + ABIOffsets.USceneComponent_ComponentToWorld_Ptr);

                                    ulong timers = a.Mesh + ABIOffsetsExt.OFF_MESH_TIMERS;
                                    r[i].AddValueEntry<float>(2, timers + (ulong)ABIOffsetsExt.OFF_LASTSUBMIT);
                                    r[i].AddValueEntry<float>(3, timers + (ulong)ABIOffsetsExt.OFF_LASTONSCREEN);
                                }

                                if (ABIOffsetsExt.OFF_CHAR_TICKING_ON_DEATH != 0)
                                    r[i].AddValueEntry<byte>(4, a.Pawn + (ulong)ABIOffsetsExt.OFF_CHAR_TICKING_ON_DEATH);

                                if (a.DeathComp != 0)
                                    r[i].AddValueEntry<byte>(5, a.DeathComp + (ulong)ABIOffsetsExt.OFF_DEATHCOMP_DEATHINFO);
                            }
                            mapA.Execute();

                            for (int i = 0; i < actors.Count; i++)
                            {
                                r[i].TryGetValue(0, out relLoc[i]);
                                r[i].TryGetValue(1, out ctwPtrs[i]);
                                r[i].TryGetValue(2, out lastSubmit[i]);
                                r[i].TryGetValue(3, out lastOnScr[i]);

                                deadFlag[i] = false;
                                if (ABIOffsetsExt.OFF_CHAR_TICKING_ON_DEATH != 0 && r[i].TryGetValue(4, out byte dbyte))
                                    deadFlag[i] = (dbyte & 0x01) != 0;

                                deadComp[i] = false;
                                if (r[i].TryGetValue(5, out byte bIsDeadByte))
                                    deadComp[i] = (bIsDeadByte & 0x01) != 0;
                            }
                        }

                        // CTW deref and bias-align
                        var ctwValues = new FTransform[actors.Count];
                        using (var mapB = DmaMemory.Scatter())
                        {
                            var r = mapB.AddRound(false);
                            for (int i = 0; i < actors.Count; i++)
                            {
                                if (ctwPtrs[i] != 0)
                                    r[i].AddValueEntry<FTransform>(0, ctwPtrs[i]);
                            }
                            mapB.Execute();

                            for (int i = 0; i < actors.Count; i++)
                            {
                                if (ctwPtrs[i] != 0 && r[i].TryGetValue(0, out FTransform ctw))
                                {
                                    if (float.IsFinite(ctw.Translation.X))
                                        ctw.Translation += frameBias;
                                    ctwValues[i] = ctw;
                                }
                                else ctwValues[i] = default;
                            }
                        }

                        // Assemble positions
                        posScratch.Clear();
                        for (int i = 0; i < actors.Count; i++)
                        {
                            var a = actors[i];
                            if (a.Root == 0) continue;

                            Vector3 pos = relLoc[i] + frameBias;

                            float H = 0f, HM = 0f;
                            if (vitals != null && vitals.TryGetValue(a.Pawn, out var vt))
                            { H = vt.h; HM = vt.hm; }

                            bool freshSkel = HasFreshSkeleton(a.Pawn);

                            posScratch.Add(new ActorPos(
                                a.Pawn, a.Mesh, a.Root, a.DeathComp,
                                pos, ctwValues[i],
                                lastSubmit[i], lastOnScr[i],
                                H, HM,
                                freshSkel,
                                deadFlag[i],
                                deadComp[i]
                            ));
                        }

                        // Publish full frame (with current camera snapshot rebased to frameBias)
                        TryGetCameraSnapshot(out var camNow, out var localNow, out _, out var camBiasNow);
                        var camSnap = camNow; camSnap.Location = camNow.Location - camBiasNow + frameBias;
                        var localSnap = localNow - camBiasNow + frameBias;

                        Interlocked.Increment(ref _frameSeq);
                        _frameBuf = new Frame
                        {
                            Cam = camSnap,
                            Local = localSnap,
                            Positions = new List<ActorPos>(posScratch),
                            Stamp = System.Diagnostics.Stopwatch.GetTimestamp()
                        };
                        Interlocked.Increment(ref _frameSeq);

                        lock (Sync) ActorPositions = new List<ActorPos>(posScratch);
                    }
                }
                catch { }

                HighResDelay(2);
            }
        }

        // republish camera-fresh frames frequently so mouse/camera shifts feel instant
        private static void FramePulseLoop()
        {
            while (_running)
            {
                try
                {
                    List<ActorPos> currentPositions;
                    lock (Sync) currentPositions = ActorPositions?.Count > 0 ? new List<ActorPos>(ActorPositions) : null;
                    if (currentPositions != null)
                    {
                        // latest camera snapshot
                        TryGetCameraSnapshot(out var cam, out var local, out _, out _);

                        Interlocked.Increment(ref _frameSeq);
                        _frameBuf = new Frame
                        {
                            Cam = cam,
                            Local = local,
                            Positions = currentPositions,
                            Stamp = System.Diagnostics.Stopwatch.GetTimestamp()
                        };
                        Interlocked.Increment(ref _frameSeq);
                    }
                }
                catch { }
                HighResDelay(1); // very small ¡ª keeps ESP reacting to mouse instantly
            }
        }

        private static void CacheSkeletonsLoop()
        {
            const int MAX_PER_SLICE = 48;
            const int MIN_PER_SLICE = 8;
            const int BUDGET_MS     = 4;
            var sw = new System.Diagnostics.Stopwatch();

            while (_running)
            {
                try
                {
                    List<ABIPlayer> actors;
                    List<ActorPos> positions;
                    Vector3 local;
                    lock (Sync)
                    {
                        if (ActorList.Count == 0) { _PruneOldSkeletons(0); HighResDelay(2); continue; }
                        actors = new List<ABIPlayer>(ActorList);
                        positions = new List<ActorPos>(ActorPositions);
                        local = LocalPosition;
                    }

                    if (positions.Count == 0) { HighResDelay(2); continue; }

                    var posMap = new Dictionary<ulong, ActorPos>(positions.Count);
                    for (int i = 0; i < positions.Count; i++) posMap[positions[i].Pawn] = positions[i];

                    actors.Sort((a, b) =>
                    {
                        posMap.TryGetValue(a.Pawn, out var pa);
                        posMap.TryGetValue(b.Pawn, out var pb);
                        float da = Vector3.DistanceSquared(local, pa.Position);
                        float db = Vector3.DistanceSquared(local, pb.Position);
                        return da.CompareTo(db);
                    });

                    sw.Restart();
                    int processed = 0, limit = Math.Min(MAX_PER_SLICE, actors.Count);

                    for (int i = 0; i < actors.Count; i++)
                    {
                        var a = actors[i];
                        if (a.Mesh == 0) continue;
                        if (!posMap.TryGetValue(a.Pawn, out var ap)) continue;
                        if (ap.IsDead) continue;

                        // use CTW from positions (already bias-aligned)
                        var ctwLocal = ap.Ctw;
                        if (Skeleton.TryGetWorldBones(a.Mesh, a.Root, in ctwLocal, out var pts, out var dbg))
                        {
                            lock (Sync) _skeletons[a.Pawn] = (pts, System.Diagnostics.Stopwatch.GetTimestamp());
                            Skeleton.LastDebug = dbg;
                        }

                        processed++;
                        if (processed >= MIN_PER_SLICE && sw.ElapsedMilliseconds >= BUDGET_MS) break;
                        if (processed >= limit) break;
                    }

                    _PruneOldSkeletons(System.Diagnostics.Stopwatch.GetTimestamp());
                }
                catch { }
            }
        }

        private static bool TryGetCameraSnapshot(out FMinimalViewInfo cam, out Vector3 local, out float ctrlYaw, out Vector3 camBias)
        {
            cam = default; local = default; ctrlYaw = 0f; camBias = default;
            for (int i = 0; i < 3; i++)
            {
                int s1 = Volatile.Read(ref _camSeq);
                if ((s1 & 1) != 0) continue;
                cam     = _camBuf;
                local   = _camLocalBuf;
                ctrlYaw = _ctrlYawBuf;
                camBias = _camBiasBuf;
                Thread.MemoryBarrier();
                int s2 = Volatile.Read(ref _camSeq);
                if (s1 == s2 && (s2 & 1) == 0) return true;
            }
            return false;
        }

        private static void _PruneOldSkeletons(long nowTicks)
        {
            long hz = System.Diagnostics.Stopwatch.Frequency;
            long stale = (long)(0.350 * hz);

            lock (Sync)
            {
                if (_skeletons.Count == 0) return;
                var toRemove = new List<ulong>(4);
                foreach (var kv in _skeletons)
                {
                    long age = (nowTicks == 0) ? long.MaxValue : nowTicks - kv.Value.ts;
                    if (age > stale) toRemove.Add(kv.Key);
                }
                for (int i = 0; i < toRemove.Count; i++) _skeletons.Remove(toRemove[i]);
            }
        }

        private static bool HasFreshSkeleton(ulong pawn, double maxAgeMs = 350.0)
        {
            lock (Sync)
            {
                if (_skeletons.TryGetValue(pawn, out var v))
                {
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    double ageMs = (now - v.ts) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    return ageMs <= maxAgeMs;
                }
            }
            return false;
        }

        private static void HighResDelay(int targetMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int sleepMs = Math.Max(0, targetMs - 1);
            if (sleepMs > 0) Thread.Sleep(sleepMs);
            while (sw.ElapsedMilliseconds < targetMs) Thread.SpinWait(80);
        }

        // data
        public readonly record struct ABIPlayer(
            ulong Pawn, ulong Mesh, ulong Root, string Name, bool IsBot,
            ulong ASC, ulong HealthSet, ulong DeathComp
        );

        public readonly record struct ActorPos(
            ulong Pawn,
            ulong Mesh,
            ulong Root,
            ulong DeathComp,
            Vector3 Position,  // world + bias
            FTransform Ctw,    // bias-aligned; for bones only
            float LastSubmit,
            float LastOnScreen,
            float Health,
            float HealthMax,
            bool HasFreshSkeleton,
            bool DeadByNativeFlag,
            bool DeadByDeathComp
        )
        {
            public bool IsVisible => (LastOnScreen + ABIOffsetsExt.VIS_TICK) >= LastSubmit;
            public bool IsDead => (HealthMax > 1f && Health <= 0.01f) || DeadByDeathComp;
        }
    }
}
