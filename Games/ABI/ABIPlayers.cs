using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MamboDMA.Services;
using VmmSharpEx.Scatter.V2;

namespace MamboDMA.Games.ABI
{
    // Offsets used by player systems
    internal static class ABIOffsetsExt
    {
        public const int OFF_PAWN_ASC            = 0x15E0;  // ASGCharacter::AbilitySystemComponent
        public const int OFF_PAWN_DEATHCOMP      = 0x1728;  // ASGCharacter::DeathComponent (USGCharacterDeathComponent*)
        public const int OFF_ASC_ATTRSETS        = 0x0188;  // UAbilitySystemComponent::SpawnedAttributes (TArray<UAttributeSet*>)
        public const int OFF_ATTR_HEALTH         = 0x48;    // USGActorHealthAttributeSet::Health
        public const int OFF_ATTR_HEALTHMAX      = 0x4C;    // USGActorHealthAttributeSet::HealthMax
        public const int OFF_DEATHCOMP_DEATHINFO = 0x0240;  // USGCharacterDeathComponent::DeathInfo (FCharacterDeathInfo) -> bIsDead at +0

        // vis via USceneComponent "last render" timers
        public const ulong OFF_MESH_TIMERS = 0x3D8; // +0x4 lastSubmit, +0xC lastOnScreen
        public const int   OFF_LASTSUBMIT  = 0x4;
        public const int   OFF_LASTONSCREEN= 0xC;

        public const float VIS_TICK = 0.06f;

        // optional native hint (kept as 0 to disable if you want)
        public const int OFF_CHAR_TICKING_ON_DEATH = 0x16B0; // bool bTickingOnDeath
    }

    // All player-related caches & APIs
    public static class Players
    {
        // ©¤©¤ world/camera pointers (diagnostics-friendly)
        public static ulong UWorld, UGameInstance, GameState, PersistentLevel;
        public static ulong ActorArray; public static int ActorCount;
        public static ulong LocalPlayers, PlayerController, PlayerArray; public static int PlayerCount;
        public static ulong LocalPawn, LocalRoot, LocalState, LocalCameraMgr;

        // published snapshots (for UI/ESP)
        public static Vector3 LocalPosition;
        public static FMinimalViewInfo Camera;

        // public views
        public static List<ABIPlayer> ActorList = new();
        public static List<ActorPos>  ActorPositions = new();

        // skeleton cache (per Pawn)
        private static readonly Dictionary<ulong, (Vector3[] pts, long ts)> _skeletons = new();

        // sync for shared state
        public static readonly object Sync = new();

        // camera seqlock
        private static int _camSeq;
        private static Vector3 _camLocalBuf;
        private static FMinimalViewInfo _camBuf;

        // coherent frame (cam+local+positions) for ESP
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
        private static readonly bool _useLastFrameCamera = false;

        // vitals (pending) from vitals loop
        private static Dictionary<ulong, (float h, float hm)> _pendingVitals;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ API ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public static void StartCache() => Start();
        public static void Start()
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
                {
                    pts = v.pts;
                    return pts != null;
                }
            }
            pts = null;
            return false;
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

                        ulong camCache = _useLastFrameCamera
                            ? ABIOffsets.APlayerCameraManager_LastFrameCameraCachePrivate
                            : ABIOffsets.APlayerCameraManager_CameraCachePrivate;

                        r[0].AddValueEntry<FMinimalViewInfo>(0, LocalCameraMgr + camCache + 0x10);
                        r[0].AddValueEntry<Vector3>(1, LocalRoot + ABIOffsets.USceneComponent_RelativeLocation);
                        map.Execute();

                        if (r[0].TryGetValue(0, out FMinimalViewInfo cam) &&
                            r[0].TryGetValue(1, out Vector3 local))
                        {
                            Interlocked.Increment(ref _camSeq);
                            _camBuf = cam; _camLocalBuf = local;
                            Camera = cam; LocalPosition = local;
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
            var ctwPtrs = new List<ulong>(256);
            var idxMap  = new List<int>(256);

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
                        // Round 1: CTW pointers
                        using (var map = DmaMemory.Scatter())
                        {
                            var round = map.AddRound(false);
                            for (int i = 0; i < actors.Count; i++)
                                if (actors[i].Mesh != 0)
                                    round[i].AddValueEntry<ulong>(0, actors[i].Mesh + ABIOffsets.USceneComponent_ComponentToWorld_Ptr);
                            map.Execute();

                            ctwPtrs.Clear();
                            idxMap.Clear();

                            for (int i = 0; i < actors.Count; i++)
                                if (round[i].TryGetValue(0, out ulong p) && p != 0)
                                { idxMap.Add(i); ctwPtrs.Add(p); }
                        }

                        // Round 2: CTW + vis timers + Native(Opt) + DeathInfo.bIsDead
                        var ctwValues = new FTransform[ctwPtrs.Count];
                        var lastSubmit = new float[ctwPtrs.Count];
                        var lastOnScr  = new float[ctwPtrs.Count];
                        var deadFlag   = new bool[ctwPtrs.Count];
                        var deadComp   = new bool[ctwPtrs.Count];

                        if (ctwPtrs.Count > 0)
                        {
                            using var map2 = DmaMemory.Scatter();
                            var r2 = map2.AddRound(false);

                            for (int j = 0; j < ctwPtrs.Count; j++)
                            {
                                int i = idxMap[j];
                                r2[j].AddValueEntry<FTransform>(0, ctwPtrs[j]); // CTW
                                ulong timers = actors[i].Mesh + ABIOffsetsExt.OFF_MESH_TIMERS;
                                r2[j].AddValueEntry<float>(1, timers + (ulong)ABIOffsetsExt.OFF_LASTSUBMIT);
                                r2[j].AddValueEntry<float>(2, timers + (ulong)ABIOffsetsExt.OFF_LASTONSCREEN);

                                if (ABIOffsetsExt.OFF_CHAR_TICKING_ON_DEATH != 0)
                                    r2[j].AddValueEntry<byte>(3, actors[i].Pawn + (ulong)ABIOffsetsExt.OFF_CHAR_TICKING_ON_DEATH);

                                if (actors[i].DeathComp != 0)
                                    r2[j].AddValueEntry<byte>(4, actors[i].DeathComp + (ulong)ABIOffsetsExt.OFF_DEATHCOMP_DEATHINFO);
                            }

                            map2.Execute();

                            for (int j = 0; j < ctwPtrs.Count; j++)
                            {
                                r2[j].TryGetValue(0, out ctwValues[j]);
                                r2[j].TryGetValue(1, out lastSubmit[j]);
                                r2[j].TryGetValue(2, out lastOnScr[j]);

                                if (ABIOffsetsExt.OFF_CHAR_TICKING_ON_DEATH != 0 &&
                                    r2[j].TryGetValue(3, out byte dbyte))
                                    deadFlag[j] = (dbyte & 0x01) != 0;
                                else deadFlag[j] = false;

                                deadComp[j] = false;
                                if (r2[j].TryGetValue(4, out byte bIsDeadByte))
                                    deadComp[j] = (bIsDeadByte & 0x01) != 0; // UE bool
                            }
                        }

                        // Build positions + merge vitals
                        posScratch.Clear();

                        for (int k = 0; k < idxMap.Count; k++)
                        {
                            int i = idxMap[k];
                            var a = actors[i];
                            var ctw = ctwValues[k];

                            Vector3 pos;
                            if (float.IsFinite(ctw.Translation.X) && Math.Abs(ctw.Rotation.W) > 1e-6f)
                                pos = ctw.Translation;
                            else
                                pos = DmaMemory.Read<Vector3>(a.Mesh + ABIOffsets.USceneComponent_RelativeLocation);

                            float H = 0f, HM = 0f;
                            if (vitals != null && vitals.TryGetValue(a.Pawn, out var vt))
                            { H = vt.h; HM = vt.hm; }

                            bool freshSkel = HasFreshSkeleton(a.Pawn);

                            posScratch.Add(new ActorPos(
                                a.Pawn, a.Mesh, a.Root, a.DeathComp,
                                pos, ctw,
                                lastSubmit[k], lastOnScr[k],
                                H, HM,
                                freshSkel,
                                deadFlag[k],
                                deadComp[k]
                            ));
                        }

                        // publish coherent frame
                        TryGetCameraSnapshot(out var camSnap, out var localSnap);
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

        private static bool TryGetCameraSnapshot(out FMinimalViewInfo cam, out Vector3 local)
        {
            cam = default; local = default;
            for (int i = 0; i < 3; i++)
            {
                int s1 = Volatile.Read(ref _camSeq);
                if ((s1 & 1) != 0) continue;
                cam = _camBuf; local = _camLocalBuf;
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
                // tight loop
            }
        }

        private static void _PruneOldSkeletons(long nowTicks)
        {
            long ticksPerSecond = System.Diagnostics.Stopwatch.Frequency;
            long stale = (long)(0.350 * ticksPerSecond); // ~350 ms

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

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Data shapes (unchanged) ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public readonly record struct ABIPlayer(
            ulong Pawn, ulong Mesh, ulong Root, string Name, bool IsBot,
            ulong ASC, ulong HealthSet, ulong DeathComp
        );

        public readonly record struct ActorPos(
            ulong Pawn,
            ulong Mesh,
            ulong Root,
            ulong DeathComp,
            Vector3 Position,
            FTransform Ctw,
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
    }

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Skeleton helper (+ Debug) ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Skeleton helper (+ Debug) ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    internal static class Skeleton
    {
        // Native bone indices you already use
        private const int Pelvis = 1;
        private const int Spine_01 = 12, Spine_02 = 13, Spine_03 = 14, Neck = 15, Head = 16;
        private const int Thigh_L = 2, Calf_L = 4, Foot_L = 5;
        private const int Thigh_R = 7, Calf_R = 9, Foot_R = 10;
        private const int Clavicle_L = 50, UpperArm_L = 51, LowerArm_L = 52, Hand_L = 54;
        private const int Clavicle_R = 20, UpperArm_R = 21, LowerArm_R = 22, Hand_R = 24;

        // Expanded fetch list (20 bones total; was 14)
        // Order matters ¡ú we expose IDX_* below to map screen drawing + ESP expectations.
        private static readonly int[] _fetch = new int[]
        {
        // Core
        Pelvis, Spine_01, Spine_02, Spine_03, Neck, Head,

        // Left arm
        Clavicle_L, UpperArm_L, LowerArm_L, Hand_L,

        // Right arm
        Clavicle_R, UpperArm_R, LowerArm_R, Hand_R,

        // Legs
        Thigh_L, Calf_L, Foot_L,
        Thigh_R, Calf_R, Foot_R
        };

        // Public indices used elsewhere (ABIESP relies on Head/Foot_L/Foot_R)
        public const int IDX_Pelvis = 0;
        public const int IDX_Spine_01 = 1;
        public const int IDX_Spine_02 = 2;
        public const int IDX_Spine_03 = 3;
        public const int IDX_Neck = 4;
        public const int IDX_Head = 5;

        public const int IDX_Clavicle_L = 6;
        public const int IDX_UpperArm_L = 7;
        public const int IDX_LowerArm_L = 8;
        public const int IDX_Hand_L = 9;

        public const int IDX_Clavicle_R = 10;
        public const int IDX_UpperArm_R = 11;
        public const int IDX_LowerArm_R = 12;
        public const int IDX_Hand_R = 13;

        public const int IDX_Thigh_L = 14;
        public const int IDX_Calf_L = 15;
        public const int IDX_Foot_L = 16;

        public const int IDX_Thigh_R = 17;
        public const int IDX_Calf_R = 18;
        public const int IDX_Foot_R = 19;

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
            bool plausibleT = Math.Abs(t.Translation.X) < 5e6f && Math.Abs(t.Translation.Y) < 5e6f && Math.Abs(t.Translation.Z) < 5e6f;
            return finite && nonZeroScale && plausibleT;
        }

        // Uses CTW from positions loop to ensure same-frame transform
        public static bool TryGetWorldBones(ulong mesh, ulong root, in FTransform ctwOverride,
                                            out Vector3[] worldPoints, out DebugInfo dbg)
        {
            dbg = default;
            dbg.Mesh = mesh;
            worldPoints = null;

            try
            {
                using var map = DmaMemory.Scatter();
                var r = map.AddRound(false);

                ulong arr = mesh + ABIOffsets.USkeletalMeshComponent_CachedComponentSpaceTransforms;
                r[0].AddValueEntry<ulong>(2, arr + 0x0);
                r[0].AddValueEntry<int>(3, arr + 0x8);
                map.Execute();

                var ctw = ctwOverride;
                if (!IsSane(ctw)) { dbg.Note = "CTW override invalid"; return false; }
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

                const int SAMPLE = 8; // a few more since we fetch more bones now
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
            catch (Exception ex) { dbg.Note = $"Exception: {ex.Message}"; return false; }
        }

        public static void Draw(ImGuiNET.ImDrawListPtr list, Vector3[] wp, Players.FMinimalViewInfo cam, float w, float h, uint color)
        {
            void seg(int a, int b)
            {
                if (ABIMath.WorldToScreen(wp[a], cam, w, h, out var A) &&
                    ABIMath.WorldToScreen(wp[b], cam, w, h, out var B))
                {
                    list.AddLine(A, B, color, 1.5f);
                }
            }

            // spine
            seg(IDX_Pelvis, IDX_Spine_01);
            seg(IDX_Spine_01, IDX_Spine_02);
            seg(IDX_Spine_02, IDX_Spine_03);
            seg(IDX_Spine_03, IDX_Neck);
            seg(IDX_Neck, IDX_Head);

            // left arm
            seg(IDX_Spine_03, IDX_Clavicle_L);
            seg(IDX_Clavicle_L, IDX_UpperArm_L);
            seg(IDX_UpperArm_L, IDX_LowerArm_L);
            seg(IDX_LowerArm_L, IDX_Hand_L);

            // right arm
            seg(IDX_Spine_03, IDX_Clavicle_R);
            seg(IDX_Clavicle_R, IDX_UpperArm_R);
            seg(IDX_UpperArm_R, IDX_LowerArm_R);
            seg(IDX_LowerArm_R, IDX_Hand_R);

            // legs
            seg(IDX_Pelvis, IDX_Thigh_L);
            seg(IDX_Thigh_L, IDX_Calf_L);
            seg(IDX_Calf_L, IDX_Foot_L);

            seg(IDX_Pelvis, IDX_Thigh_R);
            seg(IDX_Thigh_R, IDX_Calf_R);
            seg(IDX_Calf_R, IDX_Foot_R);
        }

        private static Vector3 TransformPosition(in FTransform t, in Vector3 p)
        {
            var scaled = new Vector3(p.X * t.Scale3D.X, p.Y * t.Scale3D.Y, p.Z * t.Scale3D.Z);
            var rotated = RotateVector(t.Rotation, scaled);
            return rotated + t.Translation;
        }

        private static Vector3 RotateVector(in FQuat q, in Vector3 v)
        {
            var qv = new Vector3(q.X, q.Y, q.Z);
            var t = 2f * Vector3.Cross(qv, v);
            return v + q.W * t + Vector3.Cross(qv, t);
        }
    }

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ Name Pool ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
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
}
