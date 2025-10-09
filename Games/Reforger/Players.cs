using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MamboDMA;
using MamboDMA.Games.Reforger;

namespace ArmaReforgerFeeder
{
    /// <summary>
    /// Player discovery & snapshot producer:
    /// - Slow loop: enumerate entities + metadata + hitzones (and names if PlayerManager enabled)
    /// - HP loop:   refresh HP & IsDead
    /// - Fast loop: bone sampling + projection + per-frame ActorDto list (+ publish anchors)
    ///
    /// Extras:
    /// - Anchors are published for ownership mapping (detect carried items)
    /// - Equipped weapon inference (optional)
    /// - Optional skeleton rendering (6/10/14 points)
    ///
    /// FIXES:
    /// - Aggressive dedupe by stable body key (HitZone/ExtMgr/Anim) in both Slow and Fast loops
    /// - Suppress static/unanimated skeletons from rendering
    /// - Coalesce now also merges across different names if stable body key matches
    /// </summary>
    public static class Players
    {
        //public static bool DebugLogSlow = false;
        public static bool DebugLogDrawn = false;      // <— turn this on to log what gets drawn
        public static int DebugLogDrawnEveryMs = 1000; // how often to log (per ms)    
        public static bool DebugLogTypePointers = true;
        private struct DebugTypeRecord
        {
            public string Type;          // resolved prefab path (may be empty)
            public string ClassShort;    // last token of Type
            public ulong PrefabMgr;
            public ulong TypeClass;
            public ulong TypePtr;
            public ulong AnimComp;
            public ulong ExtDamageMgr;
            public ulong HitZone;
            public ulong MeshComp;
            public ulong MeshObj;
        }

        private static readonly ConcurrentDictionary<ulong, DebugTypeRecord> _dbgTypeByEnt = new();
        // =====================================================================
        // CONFIG (defaults chosen so "it shows people")
        // =====================================================================

        public enum CharacterFilterMode { HumanoidHeuristic, ChimeraPreferred, ChimeraOnly }

        public static CharacterFilterMode FilterMode = CharacterFilterMode.HumanoidHeuristic;
        public static bool IncludeFriendlies = false;              // false = enemies only (by faction)
        public static bool OnlyPlayersFromPlayerManager = false;   // show only actual players (not AI)
        public static bool RequireHitZones = true;                 // must have ExtDamageMgr/HitZone
        public static bool IncludeRagdolls = false;                // show ragdolls? (dead always allowed)
        public static bool AnimatedOnly = true;                    // require anim component? (dead exempt)
        public static bool RequireKnownStance = false;             // stance must decode? (often too strict)

        // Distance & frame limits
        public static float MaxDrawDistance = 350f;
        public static int FrameCap = 256;
        public static int MaxEntitiesToScan = 32768;

        // Thread pacing
        public static int FastIntervalMs = 3;     // bones & camera loop
        public static int HpIntervalMs = 35;      // HP / Dead loop
        public static int SlowIntervalMs = 140;   // enumeration + metadata

        // Skeleton drawing
        public static bool EnableSkeletons = false;
        public enum SkeletonDetail { Compact6 = 6, Lite10 = 10, Baller14 = 14 }
        public static SkeletonDetail SkeletonLevel = SkeletonDetail.Lite10;
        public static float SkeletonThickness = 1.6f;

        // UI options
        public static bool ShowWeaponBelowName = true;

        // String read sizes (mods often use long names)
        public static int TypeStringMax = 64;
        public static int FactionStringMax = 64;

        // Box heuristics
        private const float PersonHeightMeters = 1.75f;
        private const float PersonWidthAspect = 0.38f;
        private const float BoxMinHPx = 24f;
        private const float BoxMaxHPx = 220f;

        // Item → player ownership heuristics
        public static float ItemAttachRadiusM = 1.20f;        // initial claim radius
        public static float ItemAttachHysteresisM = 1.80f;    // keep-claim radius (stickier)
        public static bool OwnOnlyWeaponsForNow = true;
        public static bool ExcludeGrenadesInWeapon = true;

        // Debug
        public static bool DebugLogSlow = false;

        // =====================================================================
        // Anchors and Ownership state
        // =====================================================================
        public static Action<ActorDto[], float, float>? UiSink;
        public struct AnchorInfo
        {
            public ulong Ptr;     // player entity ptr
            public Vector3f Core; // hips (preferred) / feet avg / head fallback
            public Vector3f Head; // head world pos
            public string Name;   // nickname if available
        }

        private static volatile AnchorInfo[] _anchors = Array.Empty<AnchorInfo>();
        public static AnchorInfo[] LatestAnchors => Volatile.Read(ref _anchors);

        private static readonly ConcurrentDictionary<ulong, string> _equippedWeapon = new();   // playerPtr → weapon
        private static readonly ConcurrentDictionary<ulong, List<string>> _ownedItems = new();  // playerPtr → items
        private static readonly ConcurrentDictionary<ulong, ulong> _itemOwner = new();          // itemPtr → owner playerPtr

        public static bool TryGetEquippedWeapon(ulong playerPtr, out string weapon) => _equippedWeapon.TryGetValue(playerPtr, out weapon);
        public static IReadOnlyDictionary<ulong, List<string>> OwnedItemsByPlayer => _ownedItems;

        // =====================================================================
        // Skeleton connection sets
        // =====================================================================

        private static readonly (int a, int b)[] SkelEdges6 = {
            (0,1),(1,2),(2,3),(3,4),(3,5) // Head-Neck-Spine-Hips-L/R_Foot
        };

        private static readonly (int a, int b)[] SkelEdges10 = {
            (0,1),(1,2),(2,3),   // head chain
            (2,4),(2,5),         // arms: spine -> L/R hand
            (3,6),(6,8),         // L leg
            (3,7),(7,9)          // R leg
        };

        private static readonly (int a, int b)[] SkelEdges14 = {
            (0,1),(1,2),(2,3),          // head/torso
            (2,4),(4,5),(5,6),          // L arm
            (2,7),(7,8),(8,9),          // R arm
            (3,10),(10,11),             // L leg
            (3,12),(12,13)              // R leg
        };

        public static (int a, int b)[] ActiveSkelEdges =>
            SkeletonLevel == SkeletonDetail.Baller14 ? SkelEdges14 :
            SkeletonLevel == SkeletonDetail.Lite10 ? SkelEdges10 : SkelEdges6;

        // =====================================================================
        // Misc helpers
        // =====================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ClassName(string type)
        {
            if (string.IsNullOrEmpty(type)) return string.Empty;
            int cut = type.LastIndexOfAny(new[] { '/', '\\', ':' });
            return (cut >= 0) ? type[(cut + 1)..] : type; // keep last token only
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ClassEquals(string type, string className)
            => ClassName(type).Equals(className, StringComparison.OrdinalIgnoreCase);

        private static readonly string[] HumanoidNameTokens =
        {
            "character","soldier","human","man","unit","infantry","player"
        };

        private static readonly string[] NonHumanoidNameTokens =
        {
            // vehicles / props
            "vehicle","car","truck","heli","helicopter","plane","boat","ship",
            "turret","gun","static","prop","crate","container","box","ammobox",
            "magazine","building","door","fence","decal","effect","particle","gameentity",

            // clothing / gear
            "clothing","cloth","apparel","equipment","helmet","hat","cap","mask","goggle",
            "balaclava","vest","carrier","chestrig","plate","armor","jacket","coat","shirt",
            "pants","trouser","shorts","boots","shoes","gloves","backpack","bag","pouch",
            "holster","sheath","belt","scope","optic","suppressor","silencer","stock",

            // fauna / misc
            "animal","fauna","bird","avian","crow","seagull","ragdoll","corpse","gib"
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsAny(string s, string[] toks)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < toks.Length; i++)
                if (s.IndexOf(toks[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCharacterish(string type)
            => ContainsAny(type, HumanoidNameTokens) && !ContainsAny(type, NonHumanoidNameTokens);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsChimeraStrict(string type)
        {
            var cls = ClassName(type);
            return cls.Equals("ChimeraCharacter", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("SCR_ChimeraCharacter", StringComparison.OrdinalIgnoreCase);
        }

        // Cheap but strong gate: must resolve a head (+ hips or feet) in the skeleton.
        private static bool HasHumanoidSkeleton(ulong ent, string type)
        {
            return Bones.EnsureIndicesForModel(ent, type ?? "", out var map)
                   && map.Length > 0
                   && map[0] >= 0 // Head
                   && ((map.Length > 3 && map[3] >= 0) // Hips
                        || (map.Length > 5 && map[4] >= 0 && map[5] >= 0)); // Feet
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3f MulPoint(in Matrix3x4 parent, in Matrix3x4 bone)
        {
            float x = bone.m09, y = bone.m10, z = bone.m11;
            return new Vector3f(
                parent.m00 * x + parent.m03 * y + parent.m06 * z + parent.m09,
                parent.m01 * x + parent.m04 * y + parent.m07 * z + parent.m10,
                parent.m02 * x + parent.m05 * y + parent.m08 * z + parent.m11
            );
        }

        private static bool IsZero(in Matrix3x4 m) =>
            m.m00 == 0 && m.m01 == 0 && m.m02 == 0 && m.m03 == 0 &&
            m.m04 == 0 && m.m05 == 0 && m.m06 == 0 && m.m07 == 0 &&
            m.m08 == 0 && m.m09 == 0 && m.m10 == 0 && m.m11 == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NowSeconds() =>
            System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        // =====================================================================
        // Shared state & caches
        // =====================================================================

        private sealed class MetaSnapshot
        {
            public readonly ulong[] Ents;
            public readonly string[] Types;
            public readonly string[] Factions;
            public readonly ulong[] HitZone; // cached once
            public readonly string[] Names;  // optional PM names

            public int Count => Ents?.Length ?? 0;

            public MetaSnapshot(ulong[] e, string[] t, string[] f, ulong[] hz, string[] names)
            { Ents = e; Types = t; Factions = f; HitZone = hz; Names = names; }

            public static readonly MetaSnapshot Empty =
                new(Array.Empty<ulong>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ulong>(), Array.Empty<string>());
        }

        private sealed class FrameSnapshot
        {
            public readonly ActorDto[] Actors;
            public FrameSnapshot(ActorDto[] a) { Actors = a; }
            public static readonly FrameSnapshot Empty = new(Array.Empty<ActorDto>());
        }

        private static MetaSnapshot _meta = MetaSnapshot.Empty;       // written by SLOW
        private static FrameSnapshot _frame = FrameSnapshot.Empty;    // written by FAST
        private static readonly ConcurrentDictionary<ulong, float> _hp = new();
        private static readonly ConcurrentDictionary<ulong, bool> _dead = new();

        private struct VelHist { public Vector3f Pos; public double T; public Vector3f Vel; }
        private static readonly ConcurrentDictionary<ulong, VelHist> _vel = new();

        // =====================================================================
        // Lifecycle & threads
        // =====================================================================

        private static Thread? _fastT, _hpT, _slowT;
        private static volatile bool _run;

        public static void StartWorkers()
        {
            if (_run) return;
            _run = true;

            TimerResolution.Enable1ms();
            _slowT = new Thread(SlowLoop) { IsBackground = true, Name = "Players.Slow", Priority = ThreadPriority.BelowNormal };
            _hpT = new Thread(HpLoop) { IsBackground = true, Name = "Players.HP", Priority = ThreadPriority.AboveNormal };
            _fastT = new Thread(FastLoop) { IsBackground = true, Name = "Players.Fast", Priority = ThreadPriority.Highest };
            _slowT.Start();
            _hpT.Start();
            _fastT.Start();
        }

        public static void StopWorkers()
        {
            _run = false;
            try { _fastT?.Join(150); _hpT?.Join(150); _slowT?.Join(150); } catch { /* ignore */ }
            TimerResolution.Disable1ms();
        }

        internal static class TimerResolution
        {
            [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
            [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);
            public static void Enable1ms() { try { timeBeginPeriod(1); } catch { } }
            public static void Disable1ms() { try { timeEndPeriod(1); } catch { } }
        }

        // =====================================================================
        // Public API
        // =====================================================================

        public static void PublishLatestToUI()
        {
            var fr = Volatile.Read(ref _frame);
            var actors = fr?.Actors ?? Array.Empty<ActorDto>();
            UiSink?.Invoke(actors, Game.Screen.W, Game.Screen.H);
        }

        public static void ResetSession(bool hard, string why)
        {
            Console.WriteLine($"[Players] ResetSession(hard: {hard}) due to: {why}");
            _vel.Clear();
            _hp.Clear();
            _dead.Clear();
            Bones.ClearCache();

            _equippedWeapon.Clear();
            _ownedItems.Clear();
            foreach (var kv in _itemOwner.ToArray()) _itemOwner.TryRemove(kv.Key, out _);

            ExchangeMeta(MetaSnapshot.Empty);
            ExchangeFrame(FrameSnapshot.Empty);
            PublishAnchors(Array.Empty<AnchorInfo>());

            Game.Reset();

            StopWorkers();
            StartWorkers();
        }

        // =====================================================================
        // SLOW LOOP — enumerate + metadata + hitzones
        // =====================================================================
        private static string TryResolveTypeLive(ulong ent, out ulong prefabMgr, out ulong typeClass, out ulong typePtr)
        {
            prefabMgr = typeClass = typePtr = 0;
            if (DmaMemory.Read(ent + Off.PrefabMgr, out prefabMgr) && prefabMgr != 0 &&
                DmaMemory.Read(prefabMgr + Off.PrefabDataClass, out typeClass) && typeClass != 0 &&
                DmaMemory.Read(typeClass + Off.PrefabDataType, out typePtr) && typePtr != 0)
            {
                var s = DmaMemory.ReadString(typePtr, TypeStringMax, Encoding.ASCII) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(s))
                    s = DmaMemory.ReadString(typePtr, TypeStringMax, Encoding.UTF8) ?? string.Empty;
                return s;
            }
            return string.Empty;
        }
        private static void SlowLoop()
        {
            var sw = new System.Diagnostics.Stopwatch();
            int dbgTick = 0;

            while (_run)
            {
                sw.Restart();
                Game.UpdateCamera();

                if (!DmaMemory.Read(DmaMemory.Base + Off.Game, out ulong game) || game == 0) goto SleepSlow;
                if (!DmaMemory.Read(game + Off.GameWorld, out ulong gw) || gw == 0) goto SleepSlow;

                // local faction for filtering
                string localFaction = TryGetLocalFaction(gw);

                // entity list + count
                ulong list = 0; int count = 0;
                DmaMemory.ScatterRound(rd =>
                {
                    rd[0].AddValueEntry<ulong>(0, gw + Off.EntityList);
                    rd[0].AddValueEntry<int>(1, gw + Off.EntityCount);
                    rd[0].Completed += (_, cb) =>
                    {
                        cb.TryGetValue<ulong>(0, out list);
                        cb.TryGetValue<int>(1, out count);
                    };
                }, useCache: false);

                if (list == 0 || count <= 0) { ExchangeMeta(MetaSnapshot.Empty); goto SleepSlow; }

                // read all or stride-sample
                ulong[] ents;
                if (count <= MaxEntitiesToScan)
                {
                    ents = DmaMemory.ReadArray<ulong>(list, count) ?? Array.Empty<ulong>();
                }
                else
                {
                    int take = MaxEntitiesToScan;
                    ents = new ulong[take];
                    double stride = (double)count / take;
                    using var samp = DmaMemory.Scatter();
                    var r = samp.AddRound(useCache: false);
                    for (int i = 0; i < take; i++)
                    {
                        int srcIndex = (int)(i * stride);
                        if (srcIndex >= count) srcIndex = count - 1;
                        int idx = i;
                        r[idx].AddValueEntry<ulong>(0, list + (ulong)(srcIndex * 8));
                        r[idx].Completed += (_, cb) => cb.TryGetValue<ulong>(0, out ents[idx]);
                    }
                    samp.Execute();
                }
                if (ents.Length == 0) { ExchangeMeta(MetaSnapshot.Empty); goto SleepSlow; }

                // Stage A: read positions & distance cull
                var posTmp = new Vector3f[ents.Length];
                using (var mPos = DmaMemory.Scatter())
                {
                    var r = mPos.AddRound(false);
                    for (int i = 0; i < ents.Length; i++)
                    {
                        ulong e = ents[i]; if (e == 0) continue;
                        int idx = i;
                        r[idx].AddValueEntry<Vector3f>(0, e + Off.EntityPosition);
                        r[idx].Completed += (_, cb) => cb.TryGetValue<Vector3f>(0, out posTmp[idx]);
                    }
                    mPos.Execute();
                }

                var keep = new List<int>(ents.Length);
                var cam = Game.Camera.Position;
                bool camSane =
                    !float.IsNaN(cam.X) && !float.IsNaN(cam.Y) && !float.IsNaN(cam.Z) &&
                    !(cam.X == 0 && cam.Y == 0 && cam.Z == 0) &&
                    Game.Camera.Fov > 1f && Game.Camera.Fov < 179f;

                float maxSq = MaxDrawDistance * MaxDrawDistance;

                for (int i = 0; i < ents.Length; i++)
                {
                    if (ents[i] == 0) continue;
                    if (!camSane) { keep.Add(i); continue; } // if camera is bogus, don't cull by distance
                    float dx = posTmp[i].X - cam.X, dy = posTmp[i].Y - cam.Y, dz = posTmp[i].Z - cam.Z;
                    if ((dx * dx + dy * dy + dz * dz) <= maxSq) keep.Add(i);
                }
                if (keep.Count == 0) // safeguard
                {
                    int head = Math.Min(ents.Length, 512);
                    for (int i = 0; i < head; i++) if (ents[i] != 0) keep.Add(i);
                }

                // Stage B: gather components (prefab/faction/hitzone/anim) + dead flag
                var prefabMgr = new ulong[keep.Count];
                var factionComp = new ulong[keep.Count];
                var hitZone = new ulong[keep.Count];
                var animComp = new ulong[keep.Count];
                var typeClass = new ulong[keep.Count];
                var extMgr = new ulong[keep.Count];
                var isDeadB = new bool[keep.Count];

                using (var mapB = DmaMemory.Scatter())
                {
                    var r = mapB.AddRound(false);
                    for (int k = 0; k < keep.Count; k++)
                    {
                        int idx = k; ulong e = ents[keep[k]];
                        r[idx].AddValueEntry<ulong>(0, e + Off.PrefabMgr);
                        r[idx].AddValueEntry<ulong>(1, e + Off.FactionComponent);
                        r[idx].AddValueEntry<ulong>(2, e + Off.ExtDamageMgr);
                        r[idx].AddValueEntry<ulong>(3, e + Off.CharacterAnimationComponent);
                        r[idx].AddValueEntry<ulong>(4, e + Off.ExtDamageMgr); // again for inline read

                        r[idx].Completed += (_, cb) =>
                        {
                            cb.TryGetValue<ulong>(0, out prefabMgr[idx]);
                            cb.TryGetValue<ulong>(1, out factionComp[idx]);
                            cb.TryGetValue<ulong>(3, out animComp[idx]);

                            if (cb.TryGetValue<ulong>(4, out var ext) && ext != 0)
                            {
                                extMgr[idx] = ext;
                                DmaMemory.Read(ext + Off.HitZone, out hitZone[idx]);
                            }

                            if (hitZone[idx] != 0 && DmaMemory.Read(hitZone[idx] + Off.Isdead, out byte db))
                                isDeadB[idx] = db != 0;
                        };
                    }
                    mapB.Execute();
                }

                // Resolve prefab type names
                var types = new string[keep.Count];
                var typePtr = new ulong[keep.Count];   // <— HOISTED so it's visible later
                {
                    using (var m1 = DmaMemory.Scatter())
                    {
                        var r = m1.AddRound(false);
                        for (int k = 0; k < keep.Count; k++)
                        {
                            if (prefabMgr[k] == 0) continue;
                            int idx = k;
                            r[idx].AddValueEntry<ulong>(0, prefabMgr[idx] + Off.PrefabDataClass);
                            r[idx].Completed += (_, cb) =>
                            {
                                if (cb.TryGetValue<ulong>(0, out var cls) && cls != 0)
                                {
                                    typeClass[idx] = cls;
                                    DmaMemory.Read(cls + Off.PrefabDataType, out typePtr[idx]);
                                }
                            };
                        }
                        m1.Execute();
                    }
                    using (var m2 = DmaMemory.Scatter())
                    {
                        var r = m2.AddRound();
                        for (int k = 0; k < keep.Count; k++)
                        {
                            if (typePtr[k] == 0) continue;
                            int idx = k;
                            r[idx].AddStringEntry(0, typePtr[idx], TypeStringMax, Encoding.ASCII);
                            r[idx].Completed += (_, cb) => cb.TryGetString(0, out types[idx]);
                        }
                        m2.Execute();
                    }
                }

                // Resolve faction names
                var factions = new string[keep.Count];
                {
                    var ftypePtr = new ulong[keep.Count];
                    using (var m1 = DmaMemory.Scatter())
                    {
                        var r = m1.AddRound();
                        for (int k = 0; k < keep.Count; k++)
                        {
                            if (factionComp[k] == 0) continue; int idx = k;
                            r[idx].AddValueEntry<ulong>(0, factionComp[idx] + Off.FactionComponentDataClass);
                            r[idx].Completed += (_, cb) =>
                            {
                                if (cb.TryGetValue<ulong>(0, out var fcls) && fcls != 0)
                                    DmaMemory.Read(fcls + Off.FactionComponentDataType, out ftypePtr[idx]);
                            };
                        }
                        m1.Execute();
                    }
                    using (var m2 = DmaMemory.Scatter())
                    {
                        var r = m2.AddRound();
                        for (int k = 0; k < keep.Count; k++)
                        {
                            if (ftypePtr[k] == 0) continue; int idx = k;
                            r[idx].AddStringEntry(0, ftypePtr[idx], FactionStringMax, Encoding.ASCII);
                            r[idx].Completed += (_, cb) => cb.TryGetString(0, out factions[idx]);
                        }
                        m2.Execute();
                    }
                }

                // Optional: PlayerManager names
                Dictionary<ulong, string> pmMap = OnlyPlayersFromPlayerManager ? BuildNameMap() : new();

                // Final filter (simple and robust)
                var final = new List<int>(keep.Count);

                for (int k = 0; k < keep.Count; k++)
                {
                    var type = types[k] ?? "";
                    ulong ent = ents[keep[k]];

                    if (ClassEquals(type, "GenericEntityClass")) continue;

                    bool hasAnim = animComp[k] != 0;
                    bool hasHit = hitZone[k] != 0;
                    bool isDead = isDeadB[k];

                    // identity test
                    bool charName = IsCharacterish(type);
                    bool chimera = IsChimeraStrict(type);
                    bool humanSkel = HasHumanoidSkeleton(ent, type);

                    bool passType = FilterMode switch
                    {
                        CharacterFilterMode.ChimeraOnly => chimera,
                        CharacterFilterMode.ChimeraPreferred => chimera || (charName && humanSkel),
                        _ => (charName || humanSkel) && !ContainsAny(type, NonHumanoidNameTokens),
                    };
                    if (!passType) continue;

                    if (RequireHitZones && !hasHit) continue;                        // props/gear/fauna lack hitzones typically
                    if (!IncludeRagdolls && !hasAnim && !isDead) continue;           // allow dead ragdolls even if not animated
                    if (OnlyPlayersFromPlayerManager && (pmMap == null || !pmMap.ContainsKey(ent))) continue;

                    // faction filter
                    bool sameFaction = !string.IsNullOrEmpty(factions[k]) &&
                                       factions[k].Equals(localFaction, StringComparison.OrdinalIgnoreCase);
                    if (!IncludeFriendlies && sameFaction) continue;

                    final.Add(k);
                }

                // DEDUPE #1 (slow-loop): prefer animated + skeleton + hitzones
                var bestByKey = new Dictionary<ulong, (int idx, int score)>();
                for (int ii = 0; ii < final.Count; ii++)
                {
                    int k = final[ii];
                    ulong key =
                        (hitZone[k] != 0) ? hitZone[k] :
                        (extMgr[k] != 0) ? extMgr[k] :
                        (animComp[k] != 0) ? animComp[k] : ents[keep[k]];

                    int score = 0;
                    if (animComp[k] != 0) score += 3;
                    if (HasHumanoidSkeleton(ents[keep[k]], types[k])) score += 2;
                    if (hitZone[k] != 0) score += 1;

                    if (!bestByKey.TryGetValue(key, out var cur) || score > cur.score)
                        bestByKey[key] = (k, score);
                }

                var final2 = new List<int>(bestByKey.Count);
                foreach (var v in bestByKey.Values) final2.Add(v.idx);
                final = final2;

                // Build outputs
                var entsOut = new ulong[final.Count];
                var typesOut = new string[final.Count];
                var factionsOut = new string[final.Count];
                var hitZoneOut = new ulong[final.Count];
                var namesOut = new string[final.Count];

                // Fill debug map for each kept entity so we can print class/type later in Fast loop logs
                for (int j = 0; j < final.Count; j++)
                {
                    int k = final[j];
                    ulong e = ents[keep[k]];

                    // ---- meta arrays (used by the fast loop) ----
                    entsOut[j] = e;
                    typesOut[j] = types[k] ?? string.Empty;
                    factionsOut[j] = (k < factions.Length && factions[k] != null) ? factions[k] : string.Empty;
                    hitZoneOut[j] = hitZone[k];

                    if (OnlyPlayersFromPlayerManager && pmMap.TryGetValue(e, out var nick) && !string.IsNullOrWhiteSpace(nick))
                        namesOut[j] = nick;
                    else if (!ActorNameResolver.TryGetName(e, typeClass[k], out namesOut[j]))
                        namesOut[j] = string.Empty;

                    // ---- debug record (so the fast-loop logger can show pointers) ----
                    var rec = new DebugTypeRecord
                    {
                        Type = types[k] ?? string.Empty,
                        ClassShort = ClassName(types[k] ?? string.Empty),
                        PrefabMgr = prefabMgr[k],
                        TypeClass = typeClass[k],
                        TypePtr = typePtr[k],          // now in scope
                        AnimComp = animComp[k],
                        ExtDamageMgr = extMgr[k],
                        HitZone = hitZone[k],
                        MeshComp = 0,
                        MeshObj = 0
                    };

                    if (DebugLogTypePointers && DmaMemory.Read(e + Off.MeshComponent, out ulong mc) && mc != 0)
                    {
                        rec.MeshComp = mc;
                        if (DmaMemory.Read(mc + Off.MeshComponentData, out ulong mcd) && mcd != 0)
                            DmaMemory.Read(mcd + Off.MeshObject, out rec.MeshObj);
                    }

                    _dbgTypeByEnt[e] = rec;
                }

                ExchangeMeta(new MetaSnapshot(entsOut, typesOut, factionsOut, hitZoneOut, namesOut));

                if (DebugLogSlow && ((dbgTick++ & 0x1F) == 0))
                    Console.WriteLine($"[Players/Slow] world={count} kept={keep.Count} final={final.Count} fov={Game.Camera.Fov:0.0}");
                SleepSlow:
                var spent = (int)sw.ElapsedMilliseconds;
                if (spent < SlowIntervalMs) Thread.Sleep(SlowIntervalMs - spent);
            }
        }

        private static string TryGetLocalFaction(ulong gw)
        {
            if (!DmaMemory.Read(gw + Off.LocalPlayerController, out ulong ctrl) || ctrl == 0) return string.Empty;
            if (!DmaMemory.Read(ctrl + Off.LocalPlayer, out ulong local) || local == 0) return string.Empty;
            if (!DmaMemory.Read(local + Off.FactionComponentLocal, out ulong fLocal) || fLocal == 0) return string.Empty;
            if (!DmaMemory.Read(fLocal + Off.FactionComponentDataClass, out ulong fClass) || fClass == 0) return string.Empty;
            if (!DmaMemory.Read(fClass + Off.FactionComponentDataType, out ulong fType) || fType == 0) return string.Empty;
            return DmaMemory.ReadString(fType, FactionStringMax, Encoding.ASCII) ?? string.Empty;
        }

        private static void ExchangeMeta(MetaSnapshot snap) => Interlocked.Exchange(ref _meta, snap);

        // =====================================================================
        // HP LOOP — refresh only HP and dead flag (using cached hitzones)
        // =====================================================================

        private static void HpLoop()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (_run)
            {
                sw.Restart();

                var meta = Volatile.Read(ref _meta);
                int n = meta.Count;
                if (n > 0)
                {
                    using var map = DmaMemory.Scatter();
                    var rd = map.AddRound(useCache: true);

                    for (int i = 0; i < n; i++)
                    {
                        if (i >= meta.HitZone.Length) break;
                        ulong hz = meta.HitZone[i];
                        if (hz == 0) continue;
                        int idx = i;

                        rd[idx].AddValueEntry<float>(0, hz + Off.HitZoneHP);
                        rd[idx].AddValueEntry<byte>(1, hz + Off.Isdead);

                        rd[idx].Completed += (_, cb) =>
                        {
                            if (cb.TryGetValue<float>(0, out float hp))
                                _hp[meta.Ents[idx]] = hp;

                            if (cb.TryGetValue<byte>(1, out byte deadB))
                                _dead[meta.Ents[idx]] = deadB != 0;
                        };
                    }
                    map.Execute();
                }

                var spent = (int)sw.ElapsedMilliseconds;
                if (spent < HpIntervalMs) Thread.Sleep(HpIntervalMs - spent);
            }
        }

        // =====================================================================
        // FAST LOOP — camera + bones + distance; writes full frame + anchors
        // =====================================================================

        private static void FastLoop()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (_run)
            {
                sw.Restart();
                Game.UpdateCamera();

                var meta = Volatile.Read(ref _meta);
                int n = meta.Count;
                if (n == 0)
                {
                    ExchangeFrame(FrameSnapshot.Empty);
                    PublishAnchors(Array.Empty<AnchorInfo>());
                    RebuildOwnedItems(Array.Empty<AnchorInfo>());
                    SleepFast(sw);
                    continue;
                }

                int cap = Math.Min(n, FrameCap);

                // Round 0: entity matrices
                var em = new Matrix3x4[cap];
                using (var m0 = DmaMemory.Scatter())
                {
                    var r = m0.AddRound(useCache: false);
                    for (int i = 0; i < cap; i++)
                    {
                        int idx = i;
                        r[idx].AddValueEntry<Matrix3x4>(0, meta.Ents[i] + Off.EntityMatrix);
                        r[idx].Completed += (_, cb) => cb.TryGetValue<Matrix3x4>(0, out em[idx]);
                    }
                    m0.Execute();
                }

                // Round 1: resolve mesh→bonesPtr + indices + animPtr
                var headIdx = new int[cap];
                var neckIdx = new int[cap];
                var spineIdx = new int[cap];
                var hipIdx = new int[cap];
                var lFootIdx = new int[cap];
                var rFootIdx = new int[cap];

                var lHandIdx = new int[cap];
                var rHandIdx = new int[cap];
                var lKneeIdx = new int[cap];
                var rKneeIdx = new int[cap];

                var lShoulderIdx = new int[cap];
                var rShoulderIdx = new int[cap];
                var lElbowIdx = new int[cap];
                var rElbowIdx = new int[cap];

                var bonesPtr = new ulong[cap];
                var animPtr = new ulong[cap];

                for (int i = 0; i < cap; i++)
                {
                    headIdx[i] = neckIdx[i] = spineIdx[i] = hipIdx[i] = lFootIdx[i] = rFootIdx[i] = -1;
                    lHandIdx[i] = rHandIdx[i] = lKneeIdx[i] = rKneeIdx[i] = -1;
                    lShoulderIdx[i] = rShoulderIdx[i] = lElbowIdx[i] = rElbowIdx[i] = -1;
                    bonesPtr[i] = 0; animPtr[i] = 0;
                }

                using (var m1 = DmaMemory.Scatter())
                {
                    var r = m1.AddRound(useCache: true);
                    for (int i = 0; i < cap; i++)
                    {
                        int idx = i; ulong ent = meta.Ents[i];
                        r[idx].AddValueEntry<ulong>(0, ent + Off.MeshComponent);
                        r[idx].AddValueEntry<ulong>(1, ent + Off.CharacterAnimationComponent);

                        r[idx].Completed += (_, cb) =>
                        {
                            if (cb.TryGetValue<ulong>(0, out ulong mc) && mc != 0)
                                DmaMemory.Read(mc + Off.MeshComponentBones, out bonesPtr[idx]);

                            var model = (idx < meta.Types.Length) ? meta.Types[idx] : string.Empty;

                            // Compact 6 (Head, Neck, Spine, Hips, L_Foot, R_Foot)
                            if (Bones.EnsureIndicesForModel(ent, model, out var map6) && map6.Length > 0)
                            {
                                headIdx[idx] = map6[0];
                                neckIdx[idx] = (map6.Length > 1) ? map6[1] : -1;
                                spineIdx[idx] = (map6.Length > 2) ? map6[2] : -1;
                                hipIdx[idx] = (map6.Length > 3) ? map6[3] : -1;
                                lFootIdx[idx] = (map6.Length > 4) ? map6[4] : -1;
                                rFootIdx[idx] = (map6.Length > 5) ? map6[5] : -1;
                            }

                            // Upgrade from 16-set if needed
                            if ((SkeletonLevel == SkeletonDetail.Lite10 || SkeletonLevel == SkeletonDetail.Baller14) &&
                                Bones.EnsureIndicesForModelVariant(ent, model, true, out var map16) && map16.Length >= 16)
                            {
                                if (map16[1] >= 0) neckIdx[idx] = map16[1];
                                if (map16[2] >= 0) spineIdx[idx] = map16[2];

                                if (SkeletonLevel == SkeletonDetail.Lite10)
                                {
                                    lHandIdx[idx] = map16[6];
                                    rHandIdx[idx] = map16[9];
                                    lKneeIdx[idx] = map16[11];
                                    rKneeIdx[idx] = map16[14];
                                }
                                else
                                {
                                    lShoulderIdx[idx] = map16[4];
                                    lElbowIdx[idx] = map16[5];
                                    lHandIdx[idx] = map16[6];

                                    rShoulderIdx[idx] = map16[7];
                                    rElbowIdx[idx] = map16[8];
                                    rHandIdx[idx] = map16[9];

                                    lKneeIdx[idx] = map16[11];
                                    rKneeIdx[idx] = map16[14];
                                }
                            }

                            cb.TryGetValue<ulong>(1, out animPtr[idx]);
                        };
                    }
                    m1.Execute();
                }

                // Round 2: read bone matrices + stance
                var headMat = new Matrix3x4[cap];
                var neckMat = new Matrix3x4[cap];
                var spineMat = new Matrix3x4[cap];
                var hipMat = new Matrix3x4[cap];
                var lFootMat = new Matrix3x4[cap];
                var rFootMat = new Matrix3x4[cap];

                var lHandMat = new Matrix3x4[cap];
                var rHandMat = new Matrix3x4[cap];
                var lKneeMat = new Matrix3x4[cap];
                var rKneeMat = new Matrix3x4[cap];

                var lShoulderMat = new Matrix3x4[cap];
                var rShoulderMat = new Matrix3x4[cap];
                var lElbowMat = new Matrix3x4[cap];
                var rElbowMat = new Matrix3x4[cap];

                var stanceA = new EntityStance[cap];

                using (var m2 = DmaMemory.Scatter())
                {
                    var r = m2.AddRound(useCache: false);
                    for (int i = 0; i < cap; i++)
                    {
                        if (headIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(0, bonesPtr[i] + (ulong)headIdx[i] * Off.MeshComponentBonesMatrixSize);
                        if (hipIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(2, bonesPtr[i] + (ulong)hipIdx[i] * Off.MeshComponentBonesMatrixSize);
                        if (lFootIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(3, bonesPtr[i] + (ulong)lFootIdx[i] * Off.MeshComponentBonesMatrixSize);
                        if (rFootIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(4, bonesPtr[i] + (ulong)rFootIdx[i] * Off.MeshComponentBonesMatrixSize);

                        if (neckIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(5, bonesPtr[i] + (ulong)neckIdx[i] * Off.MeshComponentBonesMatrixSize);
                        if (spineIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(6, bonesPtr[i] + (ulong)spineIdx[i] * Off.MeshComponentBonesMatrixSize);
                        if (SkeletonLevel == SkeletonDetail.Lite10)
                        {
                            if (lHandIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(7, bonesPtr[i] + (ulong)lHandIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (rHandIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(8, bonesPtr[i] + (ulong)rHandIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (lKneeIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(9, bonesPtr[i] + (ulong)lKneeIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (rKneeIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(10, bonesPtr[i] + (ulong)rKneeIdx[i] * Off.MeshComponentBonesMatrixSize);
                        }
                        else if (SkeletonLevel == SkeletonDetail.Baller14)
                        {
                            if (lShoulderIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(11, bonesPtr[i] + (ulong)lShoulderIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (rShoulderIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(12, bonesPtr[i] + (ulong)rShoulderIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (lElbowIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(13, bonesPtr[i] + (ulong)lElbowIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (rElbowIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(14, bonesPtr[i] + (ulong)rElbowIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (lHandIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(7, bonesPtr[i] + (ulong)lHandIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (rHandIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(8, bonesPtr[i] + (ulong)rHandIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (lKneeIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(9, bonesPtr[i] + (ulong)lKneeIdx[i] * Off.MeshComponentBonesMatrixSize);
                            if (rKneeIdx[i] >= 0 && bonesPtr[i] != 0) r[i].AddValueEntry<Matrix3x4>(10, bonesPtr[i] + (ulong)rKneeIdx[i] * Off.MeshComponentBonesMatrixSize);
                        }


                        if (animPtr[i] != 0) r[i].AddValueEntry<int>(1, animPtr[i] + Off.CharacterStanceType);

                        int idx = i;
                        r[idx].Completed += (_, cb) =>
                        {
                            cb.TryGetValue<Matrix3x4>(0, out headMat[idx]);
                            cb.TryGetValue<Matrix3x4>(2, out hipMat[idx]);
                            cb.TryGetValue<Matrix3x4>(3, out lFootMat[idx]);
                            cb.TryGetValue<Matrix3x4>(4, out rFootMat[idx]);

                            cb.TryGetValue<Matrix3x4>(5, out neckMat[idx]);
                            cb.TryGetValue<Matrix3x4>(6, out spineMat[idx]);

                            cb.TryGetValue<Matrix3x4>(7, out lHandMat[idx]);
                            cb.TryGetValue<Matrix3x4>(8, out rHandMat[idx]);
                            cb.TryGetValue<Matrix3x4>(9, out lKneeMat[idx]);
                            cb.TryGetValue<Matrix3x4>(10, out rKneeMat[idx]);

                            cb.TryGetValue<Matrix3x4>(11, out lShoulderMat[idx]);
                            cb.TryGetValue<Matrix3x4>(12, out rShoulderMat[idx]);
                            cb.TryGetValue<Matrix3x4>(13, out lElbowMat[idx]);
                            cb.TryGetValue<Matrix3x4>(14, out rElbowMat[idx]);

                            stanceA[idx] = EntityStance.UNKNOWN;
                            if (animPtr[idx] != 0 && cb.TryGetValue<int>(1, out int raw))
                            {
                                stanceA[idx] = raw switch
                                {
                                    0 => EntityStance.STAND,
                                    1 => EntityStance.CROUCH,
                                    2 => EntityStance.PRONE,
                                    _ => EntityStance.UNKNOWN
                                };
                            }
                        };
                    }
                    m2.Execute();
                }

                // Build candidates + anchors
                var cam = Game.Camera.Position;

                // DEDUPE #2 (fast-loop): collapse AGAIN by stable body key to kill any stragglers
                var bestByBody = new Dictionary<ulong, ActorDto>(capacity: cap);
                var anchorsBag = new Dictionary<ulong, AnchorInfo>(capacity: cap); // keyed by same body key

                // local helper for "who wins" when merging same body
                static bool Better(ActorDto a, ActorDto b)
                {
                    int sa = a.Stance != EntityStance.UNKNOWN ? 1 : 0;
                    int sb = b.Stance != EntityStance.UNKNOWN ? 1 : 0;
                    if (sa != sb) return sa > sb;

                    if (a.HasBones != b.HasBones) return a.HasBones;
                    if (a.Speed2D != b.Speed2D) return a.Speed2D > b.Speed2D;
                    if (a.IsDead != b.IsDead) return !a.IsDead;
                    if (a.Health != b.Health) return a.Health > b.Health;

                    // prefer non-empty name
                    bool aName = !string.IsNullOrWhiteSpace(a.Name);
                    bool bName = !string.IsNullOrWhiteSpace(b.Name);
                    if (aName != bName) return aName;

                    return true;
                }

                for (int i = 0; i < cap; i++)
                {
                    // HEAD anchor in world (fallback to entity origin)
                    Vector3f headW = new Vector3f(em[i].m09, em[i].m10, em[i].m11);
                    bool hasHead = (headIdx[i] >= 0 && bonesPtr[i] != 0 && !IsZero(headMat[i]));
                    if (hasHead) headW = MulPoint(em[i], headMat[i]);

                    bool hasHit = (i < _meta.HitZone.Length) && _meta.HitZone[i] != 0;

                    // require (hitzone + head)
                    bool renderHumanoid = hasHit && hasHead;

                    // dead/anim flags
                    // _dead.TryGetValue(_meta.Ents[i], out bool deadFlag);

                    if (OnlyPlayersFromPlayerManager)
                        renderHumanoid &= (i < _meta.Names.Length) && !string.IsNullOrWhiteSpace(_meta.Names[i]);

                    // Animated-only except dead; stance optional
                    // if (AnimatedOnly && animPtr[i] == 0 && !deadFlag) continue;

                    if (!renderHumanoid) continue;

                    // Head → screen
                    if (!Game.WorldToScreen(headW, out float sx, out float sy)) continue;
                    if (sx < 0 || sx > Game.Screen.W || sy < 0 || sy > Game.Screen.H) continue;

                    // distance
                    float dx = cam.X - headW.X, dy = cam.Y - headW.Y, dz = cam.Z - headW.Z;
                    int dist = (int)MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                    // HP (accepts 0..1 or 0..100)
                    float hpRaw;
                    bool haveHp = _hp.TryGetValue(_meta.Ents[i], out hpRaw);
                    float hpUi = haveHp ? (hpRaw <= 1.001f ? hpRaw * 100f : hpRaw) : 100f;
                    hpUi = Misc.Clamp(hpUi, 0f, 100f);

                    // Dead flag (robust)
                    bool reallyDead = hpUi <= 0.5f;

                    // Base of the box from feet avg or hip (world → screen)
                    bool haveLF = (lFootIdx[i] >= 0 && bonesPtr[i] != 0 && !IsZero(lFootMat[i]));
                    bool haveRF = (rFootIdx[i] >= 0 && bonesPtr[i] != 0 && !IsZero(rFootMat[i]));
                    bool haveHip = (hipIdx[i] >= 0 && bonesPtr[i] != 0 && !IsZero(hipMat[i]));

                    Vector3f baseW = headW;
                    bool usedFeet = false;

                    if (haveLF || haveRF)
                    {
                        Vector3f sum = default; int c = 0;
                        if (haveLF) { var w = MulPoint(em[i], lFootMat[i]); sum = w; c++; }
                        if (haveRF) { var w = MulPoint(em[i], rFootMat[i]); sum = (c == 0) ? w : new Vector3f((sum.X + w.X) * 0.5f, (sum.Y + w.Y) * 0.5f, (sum.Z + w.Z) * 0.5f); c++; }
                        if (c > 0) { baseW = sum; usedFeet = true; }
                    }
                    else if (haveHip)
                    {
                        baseW = MulPoint(em[i], hipMat[i]);
                    }

                    float hPxFromBones = -1f;
                    if (Game.WorldToScreen(baseW, out float sxBase, out float syBase))
                        hPxFromBones = MathF.Abs(syBase - sy);

                    // Fallback scale using distance/FOV
                    float hPx, wPx;
                    if (hPxFromBones > 0f)
                    {
                        float fudge = usedFeet ? 1.00f : 2.15f; // feet = full height; hips shorter
                        hPx = Misc.Clamp(hPxFromBones * fudge, BoxMinHPx, BoxMaxHPx);
                        wPx = hPx * PersonWidthAspect;
                    }
                    else
                    {
                        var camM = Game.Camera;
                        var d = new Vector3f(headW.X - camM.Position.X, headW.Y - camM.Position.Y, headW.Z - camM.Position.Z);
                        float distF = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                        if (distF <= 1e-3f) continue;

                        float fov = camM.Fov; if (camM.CameraZoom > 0f) fov = 90f;
                        float vf = 1f / MathF.Tan((fov * MathF.PI / 180f) * 0.5f);
                        float pxPerMeter = (Game.Screen.H * 0.5f) * vf / distF;

                        hPx = Misc.Clamp(pxPerMeter * PersonHeightMeters, BoxMinHPx, BoxMaxHPx);
                        wPx = hPx * PersonWidthAspect;
                    }

                    // Choose stable core for velocity (hip > feet avg > head)
                    Vector3f core = headW;
                    if (haveHip) core = MulPoint(em[i], hipMat[i]);
                    else if (usedFeet) core = baseW;

                    // Velocity
                    var now = NowSeconds();
                    _vel.TryGetValue(_meta.Ents[i], out var h);
                    double dt = Math.Max(1e-3, now - (h.T == 0 ? now : h.T));
                    float dxR = core.X - h.Pos.X, dyR = core.Y - h.Pos.Y, dzR = core.Z - h.Pos.Z;
                    float jump = MathF.Sqrt(dxR * dxR + dyR * dyR + dzR * dzR);
                    bool reset = (h.T == 0) || dt > 0.6 || jump > 25f;
                    Vector3f rawVel = reset ? default : new Vector3f((dxR / (float)dt), (dyR / (float)dt), (dzR / (float)dt));

                    const float alpha = 0.35f;
                    Vector3f vel = reset
                        ? rawVel
                        : new Vector3f(
                            h.Vel.X + (rawVel.X - h.Vel.X) * alpha,
                            h.Vel.Y + (rawVel.Y - h.Vel.Y) * alpha,
                            h.Vel.Z + (rawVel.Z - h.Vel.Z) * alpha
                          );

                    _vel[_meta.Ents[i]] = new VelHist { Pos = core, T = now, Vel = vel };

                    Vector3f vel2D = new Vector3f(vel.X, 0f, vel.Z);
                    float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);
                    float speed2D = MathF.Sqrt(vel2D.X * vel2D.X + vel2D.Z * vel2D.Z);
                    Vector3f dir = speed > 1e-3f ? new Vector3f(vel.X / speed, vel.Y / speed, vel.Z / speed) : default;
                    Vector3f dir2 = speed2D > 1e-3f ? new Vector3f(vel2D.X / speed2D, 0f, vel2D.Z / speed2D) : default;
                    float yawRad = (speed2D > 1e-3f) ? MathF.Atan2(dir2.X, dir2.Z) : 0f;
                    float yawDeg = yawRad * (180f / MathF.PI);

                    // Optional skeleton payload — SUPPRESS if not animated to kill static ghosts
                    Vector2f[] bones2D = null;
                    Vector2f bmin = default, bmax = default; int valid = 0;
                    if (hasHead && bonesPtr[i] != 0)
                    {
                        if (SkeletonLevel == SkeletonDetail.Compact6)
                        {
                            bones2D = new Vector2f[6]; for (int b = 0; b < bones2D.Length; b++) bones2D[b] = new Vector2f(float.NaN, float.NaN);
                            (Matrix3x4 mat, bool ok)[] mats = {
                                (headMat[i],!IsZero(headMat[i])),(neckMat[i],!IsZero(neckMat[i])),
                                (spineMat[i],!IsZero(spineMat[i])),(hipMat[i],!IsZero(hipMat[i])),
                                (lFootMat[i],!IsZero(lFootMat[i])),(rFootMat[i],!IsZero(rFootMat[i])),
                            };
                            for (int b = 0; b < mats.Length; b++)
                                if (mats[b].ok && Game.WorldToScreen(MulPoint(em[i], mats[b].mat), out float px, out float py))
                                { bones2D[b] = new Vector2f(px, py); if (valid++ == 0) { bmin = bones2D[b]; bmax = bones2D[b]; } else { if (px < bmin.X) bmin.X = px; if (py < bmin.Y) bmin.Y = py; if (px > bmax.X) bmax.X = px; if (py > bmax.Y) bmax.Y = py; } }
                        }
                        else if (SkeletonLevel == SkeletonDetail.Lite10)
                        {
                            bones2D = new Vector2f[10]; for (int b = 0; b < bones2D.Length; b++) bones2D[b] = new Vector2f(float.NaN, float.NaN);
                            (Matrix3x4 mat, bool ok)[] mats = {
                                (headMat[i],!IsZero(headMat[i])),(neckMat[i],!IsZero(neckMat[i])),
                                (spineMat[i],!IsZero(spineMat[i])),(hipMat[i],!IsZero(hipMat[i])),
                                (lHandMat[i],!IsZero(lHandMat[i])),(rHandMat[i],!IsZero(rHandMat[i])),
                                (lKneeMat[i],!IsZero(lKneeMat[i])),(rKneeMat[i],!IsZero(rKneeMat[i])),
                                (lFootMat[i],!IsZero(lFootMat[i])),(rFootMat[i],!IsZero(rFootMat[i])),
                            };
                            for (int b = 0; b < mats.Length; b++)
                                if (mats[b].ok && Game.WorldToScreen(MulPoint(em[i], mats[b].mat), out float px, out float py))
                                { bones2D[b] = new Vector2f(px, py); if (valid++ == 0) { bmin = bones2D[b]; bmax = bones2D[b]; } else { if (px < bmin.X) bmin.X = px; if (py < bmin.Y) bmin.Y = py; if (px > bmax.X) bmax.X = px; if (py > bmax.Y) bmax.Y = py; } }
                        }
                        else // Baller14
                        {
                            bones2D = new Vector2f[14]; for (int b = 0; b < bones2D.Length; b++) bones2D[b] = new Vector2f(float.NaN, float.NaN);
                            (Matrix3x4 mat, bool ok)[] mats = {
                                (headMat[i],!IsZero(headMat[i])),(neckMat[i],!IsZero(neckMat[i])),
                                (spineMat[i],!IsZero(spineMat[i])),(hipMat[i],!IsZero(hipMat[i])),
                                (lShoulderMat[i],!IsZero(lShoulderMat[i])),(lElbowMat[i],!IsZero(lElbowMat[i])),(lHandMat[i],!IsZero(lHandMat[i])),
                                (rShoulderMat[i],!IsZero(rShoulderMat[i])),(rElbowMat[i],!IsZero(rElbowMat[i])),(rHandMat[i],!IsZero(rHandMat[i])),
                                (lKneeMat[i],!IsZero(lKneeMat[i])),(lFootMat[i],!IsZero(lFootMat[i])),
                                (rKneeMat[i],!IsZero(rKneeMat[i])),(rFootMat[i],!IsZero(rFootMat[i]))
                            };
                            for (int b = 0; b < mats.Length; b++)
                                if (mats[b].ok && Game.WorldToScreen(MulPoint(em[i], mats[b].mat), out float px, out float py))
                                { bones2D[b] = new Vector2f(px, py); if (valid++ == 0) { bmin = bones2D[b]; bmax = bones2D[b]; } else { if (px < bmin.X) bmin.X = px; if (py < bmin.Y) bmin.Y = py; if (px > bmax.X) bmax.X = px; if (py > bmax.Y) bmax.Y = py; } }
                        }
                    }

                    // Build actor
                    var actor = new ActorDto
                    {
                        Ptr = _meta.Ents[i],
                        Entity = (i < _meta.Types.Length) ? _meta.Types[i] : string.Empty,
                        Faction = (i < _meta.Factions.Length) ? _meta.Factions[i] : string.Empty,
                        Name = (i < _meta.Names.Length) ? _meta.Names[i] : string.Empty,

                        Health = (int)(hpUi + 0.5f),
                        IsDead = reallyDead,
                        Distance = dist,

                        Position = headW,
                        Projected = new Vector2f(sx, sy),

                        HasHead = hasHead,
                        HeadWorld = hasHead ? headW : default,
                        Head2D = new Vector2f(sx, sy),

                        // We only consider bones "present" if we actually allowed skeletons
                        Bones = bones2D,  // always provide array (may contain NaNs if bones not valid)
                        HasBones = (hPxFromBones > 0f) || (bones2D != null && valid >= 2),
                        BoxW = wPx,
                        BoxH = hPx,

                        BMin = bmin,
                        BMax = bmax,

                        Velocity = vel,
                        Speed = speed,
                        Velocity2D = new Vector3f(vel.X, 0f, vel.Z),
                        Speed2D = speed2D,
                        Dir = dir,
                        Dir2D = dir2,
                        YawRad = yawRad,
                        YawDeg = yawDeg,
                        Stance = stanceA[i],
                    };

                    // Stable body key (same as SLOW dedupe)
                    ulong bodyKey = (i < meta.HitZone.Length && meta.HitZone[i] != 0)
                        ? meta.HitZone[i]
                        : (animPtr[i] != 0 ? animPtr[i] : actor.Ptr);

                    if (bestByBody.TryGetValue(bodyKey, out var prev))
                    {
                        if (Better(actor, prev)) bestByBody[bodyKey] = actor;
                    }
                    else
                    {
                        bestByBody[bodyKey] = actor;
                    }

                    // Anchor (keep one per body key)
                    var acore = haveHip ? MulPoint(em[i], hipMat[i]) : (usedFeet ? baseW : headW);
                    var an = new AnchorInfo
                    {
                        Ptr = actor.Ptr,
                        Core = acore,
                        Head = headW,
                        Name = actor.Name
                    };
                    if (!anchorsBag.ContainsKey(bodyKey)) anchorsBag[bodyKey] = an;
                }

                // Convert dict → list
                var dedupedList = bestByBody.Values.ToList();

                // Coalesce pass (also merges different names if world/screen-close)
                var actorsCollapsed = CoalesceByHead(dedupedList, epsMeters: 0.50f, epsPx: 10f);

                DebugDumpDrawn(actorsCollapsed);

                ExchangeFrame(new FrameSnapshot(actorsCollapsed));
                PublishAnchors(anchorsBag.Values.ToArray());
                RebuildOwnedItems(LatestAnchors);

                SleepFast(sw);
            }
        }

        private static void PublishAnchors(AnchorInfo[] anchors) => Volatile.Write(ref _anchors, anchors);

        private static void ExchangeFrame(FrameSnapshot snap) => Interlocked.Exchange(ref _frame, snap);

        private static void SleepFast(System.Diagnostics.Stopwatch sw)
        {
            int spentMs = (int)sw.ElapsedMilliseconds;
            int remainMs = FastIntervalMs - spentMs;
            if (remainMs > 1) Thread.Sleep(remainMs - 1);

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            double targetMs = Math.Max(0, FastIntervalMs - sw.ElapsedMilliseconds);
            long targetTicks = start + (long)(targetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);

            while (System.Diagnostics.Stopwatch.GetTimestamp() < targetTicks)
                Thread.SpinWait(64);
        }

        // =====================================================================
        // Ownership mapping (items carried by anchors)
        // =====================================================================

        public static bool IsItemCarried(ulong itemPtr, in Vector3f pos, string kind, string name, out ulong ownerPtr)
        {
            ownerPtr = 0;

            if (OwnOnlyWeaponsForNow && !IsWeapon(kind, name)) return false;

            var anchors = LatestAnchors;
            if (anchors == null || anchors.Length == 0) return false;

            float r0 = Math.Max(1.6f, ItemAttachRadiusM);            // claim radius
            float r1 = Math.Max(r0 * 1.5f, ItemAttachHysteresisM);   // keep radius
            float r0sq = r0 * r0, r1sq = r1 * r1;

            static float Dist2(in Vector3f a, in Vector3f b)
            {
                float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                return dx * dx + dy * dy + dz * dz;
            }

            // already owned? keep if still close
            if (_itemOwner.TryGetValue(itemPtr, out var prevOwner))
            {
                var havePrev = anchors.FirstOrDefault(a => a.Ptr == prevOwner);
                if (havePrev.Ptr != 0)
                {
                    float d2 = MathF.Min(Dist2(pos, havePrev.Core), Dist2(pos, havePrev.Head));
                    if (d2 <= r1sq) { ownerPtr = prevOwner; return true; }
                }
            }

            // fresh claim: nearest anchor within r0
            float best = float.MaxValue; ulong bestOwner = 0;
            for (int i = 0; i < anchors.Length; i++)
            {
                float d2 = MathF.Min(Dist2(pos, anchors[i].Core), Dist2(pos, anchors[i].Head));
                if (d2 <= r0sq && d2 < best) { best = d2; bestOwner = anchors[i].Ptr; }
            }

            if (bestOwner != 0)
            {
                _itemOwner[itemPtr] = bestOwner;
                ownerPtr = bestOwner;
                return true;
            }

            return false;
        }
        private static long _lastDrawLogTick;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DebugDumpDrawn(ActorDto[] list)
        {
            if (!DebugLogDrawn) return;

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double sinceMs = (_lastDrawLogTick == 0)
                ? double.MaxValue
                : (now - _lastDrawLogTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            if (sinceMs < DebugLogDrawnEveryMs) return;
            _lastDrawLogTick = now;

            var sb = new StringBuilder(512 + list.Length * 96);
            sb.Append("[Players/Draw] count=").Append(list.Length);

            for (int i = 0; i < list.Length; i++)
            {
                var a = list[i];

                // Pull cached debug info (from SlowLoop). If missing or blank, try a live read.
                DebugTypeRecord rec;
                if (!_dbgTypeByEnt.TryGetValue(a.Ptr, out rec))
                    rec = default;

                string typeStr = !string.IsNullOrWhiteSpace(a.Entity) ? a.Entity
                               : !string.IsNullOrWhiteSpace(rec.Type) ? rec.Type
                               : TryResolveTypeLive(a.Ptr, out rec.PrefabMgr, out rec.TypeClass, out rec.TypePtr);

                string clsShort = !string.IsNullOrWhiteSpace(typeStr) ? ClassName(typeStr)
                               : (!string.IsNullOrWhiteSpace(rec.ClassShort) ? rec.ClassShort : string.Empty);

                // If we just resolved live and had nothing cached, stash it for future frames
                if (!_dbgTypeByEnt.ContainsKey(a.Ptr))
                {
                    rec.Type = typeStr ?? string.Empty;
                    rec.ClassShort = clsShort ?? string.Empty;

                    // mesh pointers for extra context (best-effort)
                    if (rec.MeshComp == 0 && DmaMemory.Read(a.Ptr + Off.MeshComponent, out ulong mc) && mc != 0)
                    {
                        rec.MeshComp = mc;
                        if (DmaMemory.Read(mc + Off.MeshComponentData, out ulong mcd) && mcd != 0)
                            DmaMemory.Read(mcd + Off.MeshObject, out rec.MeshObj);
                    }

                    _dbgTypeByEnt[a.Ptr] = rec;
                }

                sb.Append("\n  #").Append(i.ToString("D2"))
                  .Append(" ptr=0x").Append(a.Ptr.ToString("X"))
                  .Append(" class=").Append(clsShort ?? string.Empty)
                  .Append(" type='").Append(typeStr ?? string.Empty).Append('\'')

                  .Append(" name='").Append(a.Name ?? string.Empty).Append('\'')

                  .Append(" hp=").Append(a.Health)
                  .Append(" dead=").Append(a.IsDead ? 1 : 0)
                  .Append(" bones=").Append(a.HasBones ? 1 : 0)
                  .Append(" stance=").Append(a.Stance)
                  .Append(" dist=").Append(a.Distance)
                  .Append(" box=").Append(a.BoxW.ToString("0.0")).Append('x').Append(a.BoxH.ToString("0.0"))
                  .Append(" head2D=(").Append(a.Head2D.X.ToString("0")).Append(",").Append(a.Head2D.Y.ToString("0")).Append(")");

                if (DebugLogTypePointers)
                {
                    sb.Append("\n      pmgr=0x").Append(rec.PrefabMgr.ToString("X"))
                      .Append(" tclass=0x").Append(rec.TypeClass.ToString("X"))
                      .Append(" tptr=0x").Append(rec.TypePtr.ToString("X"))
                      .Append(" anim=0x").Append(rec.AnimComp.ToString("X"))
                      .Append(" ext=0x").Append(rec.ExtDamageMgr.ToString("X"))
                      .Append(" hz=0x").Append(rec.HitZone.ToString("X"))
                      .Append(" mesh=0x").Append(rec.MeshComp.ToString("X"))
                      .Append(" mo=0x").Append(rec.MeshObj.ToString("X"));
                }
            }

            Console.WriteLine(sb.ToString());
        }
        private static void RebuildOwnedItems(AnchorInfo[] anchors)
        {
            var items = GameObjects.LatestItemsAll ?? Array.Empty<GameObjects.ItemDto>();
            if (anchors == null || anchors.Length == 0 || items.Length == 0)
            {
                _equippedWeapon.Clear();
                _ownedItems.Clear();
                if (_itemOwner.Count > 0)
                    foreach (var kv in _itemOwner.ToArray()) _itemOwner.TryRemove(kv.Key, out _);
                return;
            }

            var currentItems = new HashSet<ulong>(items.Select(it => it.Ptr));
            foreach (var kv in _itemOwner.ToArray())
                if (!currentItems.Contains(kv.Key)) _itemOwner.TryRemove(kv.Key, out _);

            var tmpOwned = new Dictionary<ulong, List<string>>();
            var primary = new Dictionary<ulong, string>();

            foreach (var it in items)
            {
                if (OwnOnlyWeaponsForNow && !IsWeapon(it.Kind, it.Name)) continue;

                if (IsItemCarried(it.Ptr, it.Position, it.Kind, it.Name, out var owner) && owner != 0)
                {
                    if (!tmpOwned.TryGetValue(owner, out var list))
                        tmpOwned[owner] = list = new List<string>(8);
                    list.Add(it.Name);

                    if (IsGun(it.Name, it.Type) && !primary.ContainsKey(owner))
                        primary[owner] = it.Name;
                }
            }

            _ownedItems.Clear();
            foreach (var (owner, list) in tmpOwned)
                _ownedItems[owner] = list;

            _equippedWeapon.Clear();
            foreach (var (owner, gun) in primary)
                _equippedWeapon[owner] = gun;
        }

        private static bool IsWeapon(string kind, string name)
        {
            if (!string.IsNullOrEmpty(kind) && kind.Equals("Weapon", StringComparison.OrdinalIgnoreCase)) return true;
            return ContainsAny(name ?? "", new[] { "Rifle", "Carbine", "Pistol", "SMG", "MG", "Shotgun", "Sniper", "AK", "M4", "M16", "PKM", "SVD", "VSS", "Uzi", "MP5" });
        }

        private static bool IsGun(string name, string type)
        {
            string s = $"{name} {type}";
            if (string.IsNullOrWhiteSpace(s)) return false;

            if (ContainsAny(s, new[] { "Grenade", "Launcher", "Rocket", "RPG", "Mine", "Explosive", "Binocular", "Binoculars" }))
                return false;

            return ContainsAny(s, new[] { "Rifle", "Carbine", "Pistol", "SMG", "MG", "PK", "PKM", "RPK", "AK", "M4", "M16", "AR", "DMR", "Marksman", "Sniper", "SVD", "SVU", "VSS", "AS VAL", "Shotgun", "UZI", "MP5" });
        }

        // =====================================================================
        // Name resolution (PlayerManager)
        // =====================================================================

        private static bool IsPtr(ulong v) => v > 0x10000 && v < 0x0000800000000000UL;

        private static Dictionary<ulong, string> BuildNameMap()
        {
            var map = new Dictionary<ulong, string>();

            if (!DmaMemory.Read(DmaMemory.Base + Off.Game, out ulong game) || game == 0) return map;
            if (!DmaMemory.Read(game + Off.PlayerManager, out ulong pm) || pm == 0) return map;

            if (!DmaMemory.Read(pm + Off.PmPlayerCount, out int count) || count <= 0 || count > 1024) return map;
            if (!DmaMemory.Read(pm + Off.PmPlayerArray, out ulong arr) || arr == 0) return map;

            var players = DmaMemory.ReadArray<ulong>(arr, count) ?? Array.Empty<ulong>();
            if (players.Length == 0) return map;

            var names = new string[players.Length];
            var pawns = new ulong[players.Length];

            using (var s1 = DmaMemory.Scatter())
            {
                var rd = s1.AddRound();
                for (int i = 0; i < players.Length; i++)
                {
                    ulong p = players[i]; if (p == 0) continue; int idx = i;
                    rd[idx].AddValueEntry<ulong>(0, p + Off.Player_Name);
                    rd[idx].AddValueEntry<ulong>(1, p + Off.Player_FirstLevelPtr);
                    rd[idx].Completed += (_, cb) =>
                    {
                        if (cb.TryGetValue<ulong>(0, out ulong namePtr) && namePtr != 0)
                        {
                            var s = DmaMemory.ReadString(namePtr, 64, Encoding.UTF8);
                            if (IsLikelyName(s)) names[idx] = s;
                        }

                        if (cb.TryGetValue<ulong>(1, out ulong lvl1) && lvl1 != 0)
                        {
                            if (DmaMemory.Read(lvl1 + Off.FirstLevel_ControlledEntity, out ulong ctrlEnt) && ctrlEnt != 0)
                                DmaMemory.Read(ctrlEnt + Off.ControlledEntity_Ptr2, out pawns[idx]);
                        }
                    };
                }
                s1.Execute();
            }

            for (int i = 0; i < players.Length; i++)
                if (pawns[i] != 0 && !string.IsNullOrWhiteSpace(names[i]))
                    map[pawns[i]] = names[i];

            return map;

            static bool IsLikelyName(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                if (s.Length < 2 || s.Length > 32) return false;
                if (s.Contains('/') || s.Contains('\\') || s.Contains(".et", StringComparison.OrdinalIgnoreCase)) return false;
                if (s.Contains("Chimera", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
        }

        // =====================================================================
        // Utilities (coalesce near-identical heads)
        // =====================================================================

        static ActorDto[] CoalesceByHead(List<ActorDto> src, float epsMeters = 0.35f, float epsPx = 8f)
        {
            float eps2 = epsMeters * epsMeters;
            var outList = new List<ActorDto>(src.Count);

            static bool SameNameOrBlank(string a, string b)
                => string.Equals(a, b, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(a)
                || string.IsNullOrWhiteSpace(b);

            static bool CloseWorld(in ActorDto a, in ActorDto b, float e2)
            {
                float dx = a.HeadWorld.X - b.HeadWorld.X;
                float dy = a.HeadWorld.Y - b.HeadWorld.Y;
                float dz = a.HeadWorld.Z - b.HeadWorld.Z;
                return dx * dx + dy * dy + dz * dz <= e2;
            }

            static bool CloseScreen(in ActorDto a, in ActorDto b, float px)
            {
                float dx = a.Head2D.X - b.Head2D.X;
                float dy = a.Head2D.Y - b.Head2D.Y;
                return MathF.Abs(dx) <= px && MathF.Abs(dy) <= px;
            }

            static bool Better(ActorDto a, ActorDto b)
            {
                int sa = a.Stance != EntityStance.UNKNOWN ? 1 : 0;
                int sb = b.Stance != EntityStance.UNKNOWN ? 1 : 0;
                if (sa != sb) return sa > sb;

                if (a.HasBones != b.HasBones) return a.HasBones;
                if (a.Speed2D != b.Speed2D) return a.Speed2D > b.Speed2D;
                if (a.IsDead != b.IsDead) return !a.IsDead;
                if (a.Health != b.Health) return a.Health > b.Health;

                // prefer non-empty name
                bool aName = !string.IsNullOrWhiteSpace(a.Name);
                bool bName = !string.IsNullOrWhiteSpace(b.Name);
                if (aName != bName) return aName;

                return true;
            }

            for (int i = 0; i < src.Count; i++)
            {
                var a = src[i];
                bool merged = false;

                for (int j = 0; j < outList.Count; j++)
                {
                    var b = outList[j];

                    if (SameNameOrBlank(a.Name, b.Name) && (CloseWorld(a, b, eps2) || CloseScreen(a, b, epsPx)))
                    {
                        if (Better(a, b)) outList[j] = a;
                        merged = true;
                        break;
                    }
                }

                if (!merged) outList.Add(a);
            }

            return outList.ToArray();
        }

        // =====================================================================
        // BONES — indices cache (thread-safe)
        // =====================================================================

        public static class Bones
        {
            public static void ClearCache() => _indexCache.Clear();

            // 6-slot logical set (Head, Neck, Spine, Hips, L_Foot, R_Foot)
            private static readonly string[][] NameSets6 =
            {
                new[]{ "Head", "head" },
                new[]{ "Neck", "Neck1", "Spine3", "spine_2" },
                new[]{ "Spine2", "Spine", "spine" },
                new[]{ "Hips", "Pelvis", "pelvis", "Root" },
                new[]{ "L_Foot", "LeftFoot", "l_foot" },
                new[]{ "R_Foot", "RightFoot", "r_foot" },
            };

            // 16-slot logical set (Head, Neck, Spine, Hips, L/R shoulder→elbow→hand, L/R thigh→calf→foot)
            private static readonly string[][] NameSets16 =
            {
                new[]{ "Head", "head" },         new[]{ "Neck", "Neck1", "neck" },
                new[]{ "Spine3","Spine2","Spine","spine_2","spine" }, new[]{ "Hips","Pelvis","pelvis","Root" },
                new[]{ "L_UpperArm","LeftArm","LeftShoulder","l_upperarm" }, new[]{ "L_ForeArm","LeftForeArm","l_forearm" }, new[]{ "L_Hand","LeftHand","l_hand" },
                new[]{ "R_UpperArm","RightArm","RightShoulder","r_upperarm" }, new[]{ "R_ForeArm","RightForeArm","r_forearm" }, new[]{ "R_Hand","RightHand","r_hand" },
                new[]{ "L_UpperLeg","LeftUpLeg","LeftThigh","l_thigh" }, new[]{ "L_LowerLeg","LeftLeg","l_calf","l_leg" }, new[]{ "L_Foot","LeftFoot","l_foot" },
                new[]{ "R_UpperLeg","RightUpLeg","RightThigh","r_thigh" }, new[]{ "R_LowerLeg","RightLeg","r_calf","r_leg" }, new[]{ "R_Foot","RightFoot","r_foot" },
            };

            private static readonly ConcurrentDictionary<string, int[]> _indexCache =
                new(StringComparer.OrdinalIgnoreCase);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int HashBoneName(string s)
            {
                int h = 5381;
                for (int i = 0; i < s.Length; i++)
                { int c = s[i] | 0x20; unchecked { h = (int)(((uint)h * 0x21u + (uint)c) & 0xFFFFFFFF); } }
                return h;
            }

            public static bool EnsureIndicesForModel(ulong ent, string modelType, out int[] idx)
                => EnsureIndicesForModelInternal(ent, modelType, use16: false, out idx);

            public static bool EnsureIndicesForModelVariant(ulong ent, string modelType, bool want16, out int[] idx)
                => EnsureIndicesForModelInternal(ent, modelType, use16: want16, out idx);

            private static bool EnsureIndicesForModelInternal(ulong ent, string modelType, bool use16, out int[] idx)
            {
                var sets = use16 ? NameSets16 : NameSets6;
                string suffix = use16 ? "|16" : "|6";

                if (!string.IsNullOrEmpty(modelType) && _indexCache.TryGetValue(modelType + suffix, out idx))
                    return true;

                if (!DmaMemory.Read(ent + Off.MeshComponent, out ulong mc) || mc == 0) { idx = Array.Empty<int>(); return false; }
                if (!DmaMemory.Read(mc + Off.MeshComponentData, out ulong mcd) || mcd == 0) { idx = Array.Empty<int>(); return false; }
                if (!DmaMemory.Read(mcd + Off.MeshObject, out ulong mo) || mo == 0) { idx = Array.Empty<int>(); return false; }

                string moKey = $"mo:{mo:X}{suffix}";
                if (_indexCache.TryGetValue(moKey, out idx)) return true;

                if (!DmaMemory.Read(mo + Off.MeshObjectBonesCount, out uint boneCount) || boneCount == 0 || boneCount > 500)
                { idx = Array.Empty<int>(); return false; }
                if (!DmaMemory.Read(mo + Off.MeshObjectBonesList, out ulong list) || list == 0)
                { idx = Array.Empty<int>(); return false; }

                // Build lookup (hash → logical slot)
                int logicalSlots = sets.Length;
                var want = new Dictionary<int, int>(logicalSlots * 4);
                for (int slot = 0; slot < logicalSlots; slot++)
                    foreach (var alt in sets[slot])
                        want[HashBoneName(alt)] = slot;

                var map = new int[logicalSlots];
                for (int i = 0; i < logicalSlots; i++) map[i] = -1;

                for (uint i = 0; i < boneCount; i++)
                {
                    int id; // hashed bone name from engine
                    if (!DmaMemory.Read(list + 0x10 + (Off.MeshObjectBonesSize * i), out id)) continue;

                    if (want.TryGetValue(id, out int logical) && (uint)logical < (uint)logicalSlots)
                        map[logical] = (int)i;
                }

                _indexCache[moKey] = map;
                if (!string.IsNullOrEmpty(modelType))
                    _indexCache.TryAdd(modelType + suffix, map);

                idx = map;
                return true;
            }
        }
    }
    static class ActorNameResolver
    {
        // Candidates you’ve observed (order is a hint only; learning will pick the winner)
        static readonly int[] Candidates = { 0x7D8, 0x2B0, 0x468, 0x620 };

        // Per-class cache: PrefabDataClass (or any stable class key) -> chosen offset
        static readonly Dictionary<ulong, int> _ofsByClass = new();

        // Global voting stats (handy to print once to bake into your Off map later)
        static readonly Dictionary<int, int> _votes = new();

        // UTF-8 (no BOM, no throw on invalid)
        static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        public static bool TryGetName(ulong actor, ulong classKey, out string name)
        {
            name = null;

            // Fast path: cached for this class
            if (classKey != 0 && _ofsByClass.TryGetValue(classKey, out int knownOfs))
            {
                if (TryReadNameAt(actor, knownOfs, out var s) && LooksLikeDisplayName(s))
                {
                    name = s;
                    return true;
                }
            }

            // Probe all candidates; pick the first that looks good
            string best = null;
            int chosen = 0;

            foreach (var ofs in Candidates)
            {
                if (!TryReadNameAt(actor, ofs, out var s)) continue;

                // prefer a string that passes the display-name heuristic
                if (LooksLikeDisplayName(s))
                {
                    best = s;
                    chosen = ofs;
                    break;
                }

                // keep a fallback if we haven’t found a “good” one yet
                if (best == null && !string.IsNullOrWhiteSpace(s))
                {
                    best = s;
                    chosen = ofs;
                }
            }

            if (!string.IsNullOrWhiteSpace(best))
            {
                name = best;
                if (classKey != 0)
                {
                    _ofsByClass[classKey] = chosen;
                    _votes.TryGetValue(chosen, out var v); _votes[chosen] = v + 1;
                }
                return true;
            }

            return false;
        }

        static bool TryReadNameAt(ulong actor, int ofs, out string s)
        {
            s = null;
            if (!DmaMemory.Read(actor + (ulong)ofs, out ulong p) || p == 0) return false;

            // Try UTF-8 first, then ASCII as a lenient fallback
            var u8 = DmaMemory.ReadString(p, 64, Utf8);
            if (!string.IsNullOrEmpty(u8)) { s = Clean(u8); return true; }

            var asc = DmaMemory.ReadString(p, 64, Encoding.ASCII);
            if (!string.IsNullOrEmpty(asc)) { s = Clean(asc); return true; }

            return false;
        }

        static string Clean(string s)
        {
            // Trim nulls/whitespace; avoid control chars
            s = s.TrimEnd('\0').Trim();
            if (s.Length == 0) return s;
            // replace bogus control chars with space (defensive)
            var arr = s.ToCharArray();
            for (int i = 0; i < arr.Length; i++)
                if (char.IsControl(arr[i]) && arr[i] != '\t' && arr[i] != '\n' && arr[i] != '\r') arr[i] = ' ';
            return new string(arr).Trim();
        }

        static bool LooksLikeDisplayName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.Length < 3 || s.Length > 48) return false;

            // Drop obvious non-display/state tokens you’ve seen in dumps
            string[] bad = { "FakeDeath", "pilot", "Compartment", "SelectAction" };
            foreach (var b in bad)
                if (s.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return false;

            int letters = 0;
            foreach (var c in s) if (char.IsLetter(c)) letters++;
            if (letters < 2) return false;

            return true;
        }

        // (Optional) call occasionally to see which offsets are winning globally
        public static string GetVoteSummary()
        {
            var sb = new StringBuilder();
            foreach (var kv in _votes)
                sb.AppendLine($"+0x{kv.Key:X} -> {kv.Value} hits");
            return sb.ToString();
        }
    }    
}
