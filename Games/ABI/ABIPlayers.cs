using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ImGuiNET;
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

        private static Dictionary<ulong, (float h, float hm)> _pendingVitals;

        // API
        public static void StartCache()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            new Thread(CacheWorldLoop)     { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.World"     }.Start();
            new Thread(CacheCameraLoop)    { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Camera"    }.Start();
            new Thread(CachePlayersLoop)   { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.Players"   }.Start();
            new Thread(CacheVitalsLoop)    { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Vitals"    }.Start();
            new Thread(CachePositionsLoop) { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Positions" }.Start();
            new Thread(CacheSkeletonsLoop) { IsBackground = true, Priority = ThreadPriority.Highest,     Name = "ABI.Skeletons" }.Start();
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

            // (ULevel.Actors TArray is: Data at +0, Num at +8)
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
                            // world-origin jump guard (kept for safety)
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
                HighResDelay(1);
            }
        }

        private static void CachePlayersLoop()
        {
            while (_running) { try { CachePlayersScatter(); } catch { } HighResDelay(45); }
        }

        private static void CachePlayersScatter()
        {
            if (ActorArray == 0 || ActorCount <= 0) return;

            var tmp = new List<ABIPlayer>(Math.Min(ActorCount, 2048));

            using var map = DmaMemory.Scatter();
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

                tmp.Add(new ABIPlayer(pawn, 0, 0, name, name.Contains("AI"), 0, 0, 0));
            }

            if (tmp.Count == 0) { lock (Sync) ActorList = tmp; return; }

            using var map2 = DmaMemory.Scatter();
            var r2 = map2.AddRound(false);
            for (int i = 0; i < tmp.Count; i++)
            {
                r2[i].AddValueEntry<ulong>(0, tmp[i].Pawn + ABIOffsets.ACharacter_Mesh);
                r2[i].AddValueEntry<ulong>(1, tmp[i].Pawn + ABIOffsets.AActor_RootComponent);
                r2[i].AddValueEntry<ulong>(2, tmp[i].Pawn + (ulong)ABIOffsetsExt.OFF_PAWN_ASC);
                r2[i].AddValueEntry<ulong>(3, tmp[i].Pawn + (ulong)ABIOffsetsExt.OFF_PAWN_DEATHCOMP);
            }
            map2.Execute();

            for (int i = 0; i < tmp.Count; i++)
            {
                if (r2[i].TryGetValue(0, out ulong mesh)) tmp[i] = tmp[i] with { Mesh = mesh };
                if (r2[i].TryGetValue(1, out ulong root)) tmp[i] = tmp[i] with { Root = root };
                if (r2[i].TryGetValue(2, out ulong asc )) tmp[i] = tmp[i] with { ASC  = asc  };
                if (r2[i].TryGetValue(3, out ulong dc  )) tmp[i] = tmp[i] with { DeathComp = dc };
            }

            lock (Sync) ActorList = tmp;
        }

        private static void CacheVitalsLoop()
        {
            var needResolve = new List<int>(256);
            var hsReadList  = new List<(int idx, ulong hs)>(256);

            while (_running)
            {
                try
                {
                    List<ABIPlayer> actors;
                    lock (Sync) actors = ActorList.Count == 0 ? null : new List<ABIPlayer>(ActorList);
                    if (actors == null || actors.Count == 0) { HighResDelay(2); continue; }

                    needResolve.Clear();
                    hsReadList.Clear();

                    for (int i = 0; i < actors.Count; i++)
                    {
                        if (actors[i].ASC == 0) continue;
                        if (actors[i].HealthSet == 0) needResolve.Add(i);
                        else hsReadList.Add((i, actors[i].HealthSet));
                    }

                    // resolve HealthSet from ASC->SpawnedAttributes
                    if (needResolve.Count > 0)
                    {
                        using var map = DmaMemory.Scatter();
                        var rd = map.AddRound(false);

                        for (int k = 0; k < needResolve.Count; k++)
                        {
                            int i = needResolve[k];
                            ulong asc = actors[i].ASC;
                            ulong basePtr = asc + (ulong)ABIOffsetsExt.OFF_ASC_ATTRSETS;
                            rd[k].AddValueEntry<ulong>(0, basePtr + 0x0); // Data
                            rd[k].AddValueEntry<int>(1,  basePtr + 0x8); // Num
                        }
                        map.Execute();

                        const int MAX_SCAN = 8;
                        for (int k = 0; k < needResolve.Count; k++)
                        {
                            int i = needResolve[k];
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
                                actors[i] = actors[i] with { HealthSet = resolved };
                                hsReadList.Add((i, resolved));
                            }
                        }

                        lock (Sync) ActorList = actors;
                    }

                    // read Health/HealthMax
                    if (hsReadList.Count > 0)
                    {
                        using var map = DmaMemory.Scatter();
                        var r = map.AddRound(false);
                        for (int k = 0; k < hsReadList.Count; k++)
                        {
                            ulong hs = hsReadList[k].hs;
                            r[k].AddValueEntry<float>(0, hs + (ulong)ABIOffsetsExt.OFF_ATTR_HEALTH);
                            r[k].AddValueEntry<float>(1, hs + (ulong)ABIOffsetsExt.OFF_ATTR_HEALTHMAX);
                        }
                        map.Execute();

                        var vitals = new Dictionary<ulong, (float h, float hm)>(hsReadList.Count);
                        for (int k = 0; k < hsReadList.Count; k++)
                        {
                            r[k].TryGetValue(0, out float h);
                            r[k].TryGetValue(1, out float hm);
                            var pawn = actors[hsReadList[k].idx].Pawn;
                            vitals[pawn] = (h, hm);
                        }
                        lock (Sync) _pendingVitals = vitals;
                    }
                }
                catch { }

                HighResDelay(2);
            }
        }

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
                        if (_pendingVitals != null) vitals = new Dictionary<ulong, (float h, float hm)>(_pendingVitals);
                    }

                    if (actors != null && actors.Count > 0)
                    {
                        // Freeze bias snapshot for the frame
                        var bias = _originBias;

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
                                    // IMPORTANT: read CTW *pointer*, not inline struct
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

                        // Now deref CTW pointers to real FTransforms and bias-align Translation for bones
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
                                    // Bias-align CTW once so bone world positions match this frame¡¯s origin
                                    if (float.IsFinite(ctw.Translation.X))
                                        ctw.Translation += bias;
                                    ctwValues[i] = ctw;
                                }
                                else
                                {
                                    ctwValues[i] = default;
                                }
                            }
                        }

                        // Build positions (Root->RelativeLocation + bias)
                        posScratch.Clear();
                        for (int i = 0; i < actors.Count; i++)
                        {
                            var a = actors[i];
                            if (a.Root == 0) continue;

                            Vector3 pos = relLoc[i] + bias;

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

                        // Keep camera/local aligned to the same bias snapshot
                        TryGetCameraSnapshot(out var camRaw, out var localRaw, out _, out var camBias);
                        var camSnap = camRaw; camSnap.Location = camRaw.Location - camBias + bias;
                        var localSnap = localRaw - camBias + bias;

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

                HighResDelay(1);
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
                        if (ActorList.Count == 0) { _PruneOldSkeletons(0); HighResDelay(1); continue; }
                        actors = new List<ABIPlayer>(ActorList);
                        positions = new List<ActorPos>(ActorPositions);
                        local = LocalPosition;
                    }

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

                        // use CTW already bias-aligned in positions pass
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
