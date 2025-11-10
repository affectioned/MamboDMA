// File: Games/ABI/ABILoot.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using MamboDMA.Services;
using VmmSharpEx.Scatter.V2;

namespace MamboDMA.Games.ABI
{
    public static class ABILoot
    {
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Public data model
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public struct Item
        {
            public ulong   Actor;
            public ulong   ContainerActor;
            public bool    InContainer;
            public string  ClassName;
            public string  Label;
            public int     Stack;
            public Vector3 Position;
            public int     ApproxPrice;
        }

        public struct Frame
        {
            public long       StampTicks;
            public List<Item> Items;
            public int        TotalActorsSeen;
            public int        ContainersFound;
            public int        ContainersExpanded;
        }

        public interface IPriceProvider { int TryGetPrice(string className); }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Controls
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public static int  UpdateIntervalMs { get => _intervalMs; set => _intervalMs = Math.Clamp(value, 16, 500); }
        public static bool IsRunning => _running;
        public static void SetPriceProvider(IPriceProvider provider) => _priceProvider = provider;

        public static void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ABI.Loot" };
            _thread.Start();
        }

        public static void Stop()
        {
            _running = false;
            try { _thread?.Join(250); } catch { }
            _thread = null;
        }

        public static bool TryGetLoot(out Frame f)
        {
            lock (_sync) { f = _latest; return f.StampTicks != 0; }
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Internal state
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static volatile bool _running;
        private static Thread _thread;
        private static readonly object _sync = new();
        private static Frame _latest;
        private static int _intervalMs = 60;
        private static IPriceProvider _priceProvider;

        // Confirmed for ABP_ArmoredContainerBase_C. Other variants may differ ¡ú try fallbacks.
        private static readonly int[] CANDIDATE_MGR_OFFS =
        {
            0x8F0, // ABP_ArmoredContainerBase_C::SGInventoryContainerMgr (from your dump)
            0x8E8, // some builds place it here
            0x8A0, // earlier comp packs
            0x850, // very old user notes
        };

        // USGInventoryContainerMgrComponent::FInventoryContainerBase TArray<...>
        private static readonly int[] CANDIDATE_BASELIST_OFFS =
        {
            0x140, // from your note
            0x138, // alignment shifts seen in minor updates
            0x150, // occasional padding
        };

        // FInventoryContainerBase known fields
        private const int OFF_INVBASE_CHILD_ACTORS  = 0x28;  // TArray<AActor*>
        private const int SIZEOF_INVBASE            = 0x48;  // struct size/stride

        // Class hints
        private static readonly HashSet<string> LootContainerClassHints = new(StringComparer.OrdinalIgnoreCase)
        {
            "ABP_LootBoxBase_C", "BP_ContainerBase_C", "BP_ArmoredContainerBase_C",
            "ABP_ArmoredContainerBase_C", "ArmoredContainer", "ABP_LootBox_C"
        };

        private static readonly HashSet<string> LootItemBaseHints = new(StringComparer.OrdinalIgnoreCase)
        {
            "BP_ItemBase_C", "BP_AmmoBase_C", "BP_MagazineBase_C", "BP_StockBase_C",
            "BP_SightBase_C", "BP_MountBase_C", "BP_HeadsetsBase_C", "BP_HelmetBase_C",
            "BP_VestBase_C", "BP_HandGuardBase_C", "BP_MuzzleBase_C", "BP_RecoveryBase_C",
            "BP_SGWeapon_C", "BP_FaceCoverBase_C", "BP_EyewearBase_C", "BP_ContainerBase_C",
            "BP_ArmoredContainerBase_C", "BP_ReceiverCoverBase_C", "BP_ChargingHandleBase_C",
            "ABP_ArmoredContainerBase_C", "ArmoredContainer"
        };

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Loop
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static void Loop()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (_running)
            {
                sw.Restart();
                try { Build(); } catch { }
                int left = _intervalMs - (int)sw.ElapsedMilliseconds;
                if (left > 0) HighResDelay(left);
            }
        }

        private static void Build()
        {
            ulong actorArray = Players.ActorArray;
            int   actorCount = Players.ActorCount;

            if (actorArray == 0 || actorCount <= 0) { PublishEmpty(actorCount); return; }

            int take = Math.Min(actorCount, 4096);
            var ptrs = DmaMemory.ReadArray<ulong>(actorArray, take);
            if (ptrs == null || ptrs.Length == 0) { PublishEmpty(actorCount); return; }

            using var map = DmaMemory.Scatter();
            var rd = map.AddRound(false);

            for (int i = 0; i < ptrs.Length; i++)
            {
                ulong a = ptrs[i]; if (a == 0) continue;
                rd[i].AddValueEntry<uint>(0, a + 24);                           // class FName (ABINamePool)
                rd[i].AddValueEntry<ulong>(1, a + ABIOffsets.AActor_RootComponent);
            }
            map.Execute();

            var items = new List<Item>(256);
            var containers = new List<ulong>(64);
            int containersExpanded = 0;

            // classify
            for (int i = 0; i < ptrs.Length; i++)
            {
                ulong a = ptrs[i]; if (a == 0) continue;
                if (!rd[i].TryGetValue(0, out uint fname) || fname == 0) continue;
                string cls = ABINamePool.GetName(fname); if (string.IsNullOrEmpty(cls)) continue;

                if (IsContainer(cls)) { containers.Add(a); continue; }

                if (IsItem(cls))
                {
                    Vector3 pos = ReadActorWorldPosFromRoot(rd[i].TryGetValue(1, out ulong root) ? root : 0);
                    items.Add(new Item {
                        Actor = a, ContainerActor = 0, InContainer = false,
                        ClassName = cls, Label = cls, Stack = 1, Position = pos,
                        ApproxPrice = _priceProvider?.TryGetPrice(cls) ?? 0
                    });
                }
            }

            // expand containers
            for (int c = 0; c < containers.Count; c++)
            {
                if (ExpandContainer(containers[c], items)) containersExpanded++;
            }

            lock (_sync)
            {
                _latest = new Frame {
                    StampTicks       = DateTime.UtcNow.Ticks,
                    Items            = items,
                    TotalActorsSeen  = actorCount,
                    ContainersFound  = containers.Count,
                    ContainersExpanded = containersExpanded,
                };
            }
        }

        private static bool IsContainer(string cls)
        {
            if (LootContainerClassHints.Contains(cls)) return true;
            return cls.IndexOf("Inventory_", StringComparison.OrdinalIgnoreCase) >= 0
                || cls.IndexOf("LootBox",    StringComparison.OrdinalIgnoreCase) >= 0
                || cls.IndexOf("Container",  StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsItem(string cls)
        {
            if (LootItemBaseHints.Contains(cls)) return true;
            return cls.StartsWith("BP_", StringComparison.OrdinalIgnoreCase) && cls.EndsWith("_C", StringComparison.OrdinalIgnoreCase);
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Container expansion
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static bool ExpandContainer(ulong boxActor, List<Item> outList)
        {
            try
            {
                // 1) Resolve the USGInventoryContainerMgrComponent*
                ulong mgr = GetContainerMgr(boxActor);
                if (mgr == 0) return false;

                // 2) Read TArray<FInventoryContainerBase> header (with validation)
                if (!TryReadContainerBaseList(mgr, out var baseList)) return false;
                if (baseList.Count <= 0 || baseList.Data == 0) return false;

                int count = Math.Min(baseList.Count, 128);
                Vector3 contPos = ReadActorWorldPos(boxActor);

                using var map = DmaMemory.Scatter();
                var r = map.AddRound(false);

                for (int i = 0; i < count; i++)
                {
                    ulong elem = baseList.Data + (ulong)(i * SIZEOF_INVBASE);
                    r[i].AddValueEntry<ulong>(0, elem + (ulong)OFF_INVBASE_CHILD_ACTORS + 0x00); // Data
                    r[i].AddValueEntry<int>(1,   elem + (ulong)OFF_INVBASE_CHILD_ACTORS + 0x08); // Count
                }
                map.Execute();

                int totalKids = 0;

                for (int i = 0; i < count; i++)
                {
                    if (!r[i].TryGetValue(0, out ulong data) || data == 0) continue;
                    if (!r[i].TryGetValue(1, out int   c   ) || c <= 0 || c > 512) continue;

                    var kids = DmaMemory.ReadArray<ulong>(data, c);
                    if (kids == null || kids.Length == 0) continue;

                    totalKids += kids.Length;

                    using var map2 = DmaMemory.Scatter();
                    var r2 = map2.AddRound(false);
                    for (int k = 0; k < kids.Length; k++)
                    {
                        ulong a = kids[k]; if (a == 0) continue;
                        r2[k].AddValueEntry<uint>(0, a + 24);                          // class FName
                        r2[k].AddValueEntry<ulong>(1, a + ABIOffsets.AActor_RootComponent);
                    }
                    map2.Execute();

                    for (int k = 0; k < kids.Length; k++)
                    {
                        ulong a = kids[k]; if (a == 0) continue;
                        string cls = r2[k].TryGetValue(0, out uint fname) ? ABINamePool.GetName(fname) : null;
                        if (string.IsNullOrEmpty(cls) || !IsItem(cls)) continue;

                        Vector3 pos = contPos; // cheap; or uncomment next two lines for per-item transform
                        // if (r2[k].TryGetValue(1, out ulong root) && root != 0)
                        //     pos = ReadActorWorldPosFromRoot(root);

                        outList.Add(new Item {
                            Actor = a, ContainerActor = boxActor, InContainer = true,
                            ClassName = cls, Label = cls, Stack = 1, Position = pos,
                            ApproxPrice = _priceProvider?.TryGetPrice(cls) ?? 0
                        });
                    }
                }

                return totalKids > 0;
            }
            catch { return false; }
        }

        private static ulong GetContainerMgr(ulong boxActor)
        {
            for (int i = 0; i < CANDIDATE_MGR_OFFS.Length; i++)
            {
                ulong p = DmaMemory.Read<ulong>(boxActor + (ulong)CANDIDATE_MGR_OFFS[i]);
                // minimal sanity: pointer & kernel hints (canonical user space), non-null
                if (p != 0 && (p & 0xFFF0000000000000UL) == 0) return p;
            }
            return 0;
        }

        private static bool TryReadContainerBaseList(ulong mgr, out TArrayHdr hdr)
        {
            // try all known offsets; validate first two ints in first element look sane
            for (int i = 0; i < CANDIDATE_BASELIST_OFFS.Length; i++)
            {
                hdr = ReadTArray(mgr + (ulong)CANDIDATE_BASELIST_OFFS[i]);
                if (hdr.Data == 0 || hdr.Count <= 0) continue;

                // quick validation: peek RowNum/ColumnNum in element 0
                ulong elem0 = hdr.Data;
                int row = DmaMemory.Read<int>(elem0 + 0x00);
                int col = DmaMemory.Read<int>(elem0 + 0x04);
                // Row/Col are small positive (normally <= ~20)
                if (row >= 0 && row <= 64 && col >= 0 && col <= 64)
                    return true;
            }

            hdr = default;
            return false;
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Position helpers
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static Vector3 ReadActorWorldPos(ulong actor)
        {
            if (actor == 0) return default;
            ulong root = DmaMemory.Read<ulong>(actor + ABIOffsets.AActor_RootComponent);
            return ReadActorWorldPosFromRoot(root);
        }

        private static Vector3 ReadActorWorldPosFromRoot(ulong rootComp)
        {
            if (rootComp == 0) return default;
            ulong ctwPtr = DmaMemory.Read<ulong>(rootComp + ABIOffsets.USceneComponent_ComponentToWorld_Ptr);
            if (ctwPtr == 0) return default;

            var t = DmaMemory.Read<FTransform>(ctwPtr);

            // keep same bias as players ¡ú one-time radar X/Y
            Vector3 bias = Players.LocalPosition - Players.Camera.Location;

            if (!float.IsFinite(t.Translation.X) || !float.IsFinite(t.Translation.Y) || !float.IsFinite(t.Translation.Z))
                return default;

            return t.Translation + bias;
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // TArray helper
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private readonly struct TArrayHdr
        {
            public readonly ulong Data;
            public readonly int   Count;
            public readonly int   Max;
            public TArrayHdr(ulong d, int c, int m) { Data = d; Count = c; Max = m; }
        }

        private static TArrayHdr ReadTArray(ulong addr)
        {
            Span<byte> buf = stackalloc byte[16];
            if (!DmaMemory.Read(addr, buf)) return default;
            ulong data = Unsafe.ReadUnaligned<ulong>(ref buf[0]);
            int count  = Unsafe.ReadUnaligned<int>(ref buf[8]);
            int max    = Unsafe.ReadUnaligned<int>(ref buf[12]);
            if (count < 0 || count > max || count > 1_000_000) return default;
            return new TArrayHdr(data, count, max);
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Publish & timing
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static void PublishEmpty(int totalActors)
        {
            lock (_sync)
            {
                _latest = new Frame {
                    StampTicks = DateTime.UtcNow.Ticks,
                    Items = new List<Item>(0),
                    TotalActorsSeen = Math.Max(totalActors, 0),
                    ContainersFound = 0,
                    ContainersExpanded = 0
                };
            }
        }

        private static void HighResDelay(int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int s = Math.Max(0, ms - 1);
            if (s > 0) Thread.Sleep(s);
            while (sw.ElapsedMilliseconds < ms) Thread.SpinWait(80);
        }
    }
}
