// File: Games/ABI/ABILoot.cs
using System;
using System.Collections.Generic;
using System.Linq;
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
        public static bool EnableDebugLogging = false; // Toggle for diagnostics

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

        // Track containers that have been searched (contents are now client-side)
        private static readonly Dictionary<ulong, (long timestamp, List<Item> items)> _searchedContainers = new(256);
        private static readonly TimeSpan _searchedContainerExpiry = TimeSpan.FromMinutes(5);

        // CONFIRMED: ABP_ArmoredContainerBase_C
        // SGInventoryContainerMgr at offset 0x8F0
        private static readonly int[] CANDIDATE_MGR_OFFS =
        {
            0x8F0, // Primary: from your dump
            0x8E8, // Fallback
            0x8A0,
        };

        // CONFIRMED: USGInventoryContainerMgrComponent
        // InventoryContainerBaseList (TArray<FInventoryContainerBase>) at 0x140
        private const int OFF_MGR_BASELIST = 0x140;

        // FInventoryContainerBase structure
        private const int OFF_INVBASE_ROW           = 0x00;  // int32
        private const int OFF_INVBASE_COLUMN        = 0x04;  // int32
        private const int OFF_INVBASE_CHILD_ACTORS  = 0x10;  // TArray<AActor*> - CONFIRMED from dump
        private const int SIZEOF_INVBASE            = 0x48;  // struct size/stride

        // Try more offsets if needed
        private static readonly int[] CANDIDATE_CHILD_ACTOR_OFFS = 
        {
            0x10, // Primary from dump
            0x28, // Secondary guess
            0x30, 0x38, 0x20, 0x18, 0x08,
        };

        // Class hints
        private static readonly HashSet<string> LootContainerClassHints = new(StringComparer.OrdinalIgnoreCase)
        {
            "ABP_LootBoxBase_C", 
            "BP_ContainerBase_C", 
            "BP_ArmoredContainerBase_C",
            "ABP_ArmoredContainerBase_C", 
            "ArmoredContainer", 
            "ABP_LootBox_C",
            "BP_LootBox_C",
        };

        private static readonly HashSet<string> LootItemBaseHints = new(StringComparer.OrdinalIgnoreCase)
        {
            "BP_ItemBase_C", "BP_AmmoBase_C", "BP_MagazineBase_C", "BP_StockBase_C",
            "BP_SightBase_C", "BP_MountBase_C", "BP_HeadsetsBase_C", "BP_HelmetBase_C",
            "BP_VestBase_C", "BP_HandGuardBase_C", "BP_MuzzleBase_C", "BP_RecoveryBase_C",
            "BP_SGWeapon_C", "BP_FaceCoverBase_C", "BP_EyewearBase_C", 
            "BP_ReceiverCoverBase_C", "BP_ChargingHandleBase_C",
            "BP_BackPackBase_C", "BP_TacRigBase_C", "BP_PouchBase_C", // Wearable containers
        };

        // Cache the working offset once found
        private static int? _cachedChildActorOffset = null;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Loop
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static void Loop()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (_running)
            {
                sw.Restart();
                try { Build(); } 
                catch (Exception ex) 
                { 
                    if (EnableDebugLogging) 
                        Console.WriteLine($"[ABILoot] Loop error: {ex.Message}"); 
                }
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
                rd[i].AddValueEntry<uint>(0, a + 24);
                rd[i].AddValueEntry<ulong>(1, a + ABIOffsets.AActor_RootComponent);
            }
            map.Execute();

            var items = new List<Item>(256);
            var containers = new List<ulong>(64);
            int containersExpanded = 0;

            // Classify actors
            for (int i = 0; i < ptrs.Length; i++)
            {
                ulong a = ptrs[i]; if (a == 0) continue;
                if (!rd[i].TryGetValue(0, out uint fname) || fname == 0) continue;
                string cls = ABINamePool.GetName(fname); 
                if (string.IsNullOrEmpty(cls)) continue;

                if (IsContainer(cls)) 
                { 
                    containers.Add(a);
                    continue; 
                }

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

            if (EnableDebugLogging)
                Console.WriteLine($"[ABILoot] Found {containers.Count} containers, {items.Count} loose items");

            // Prune old searched containers
            var now = DateTime.UtcNow;
            var toRemove = _searchedContainers
                .Where(kvp => now - new DateTime(kvp.Value.timestamp) > _searchedContainerExpiry)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in toRemove)
                _searchedContainers.Remove(key);

            // Try to expand containers
            for (int c = 0; c < containers.Count; c++)
            {
                if (ExpandContainer(containers[c], items)) 
                    containersExpanded++;
            }

            // Add items from previously searched containers
            foreach (var kvp in _searchedContainers)
            {
                foreach (var item in kvp.Value.items)
                {
                    if (!items.Any(x => x.Actor == item.Actor)) // Avoid duplicates
                        items.Add(item);
                }
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
            if (string.IsNullOrEmpty(cls)) return false;
            if (LootItemBaseHints.Contains(cls)) return true;
            
            // More lenient check - most items follow these patterns
            if (cls.StartsWith("BP_", StringComparison.OrdinalIgnoreCase) && 
                cls.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
            {
                // Exclude known non-items
                if (cls.Contains("LootBox", StringComparison.OrdinalIgnoreCase)) return false;
                
                return true; // It's probably an item
            }
            
            return false;
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Container expansion - ENHANCED with detailed diagnostics
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static bool ExpandContainer(ulong boxActor, List<Item> outList)
        {
            try
            {
                // 1) Get manager component pointer
                ulong mgr = GetContainerMgr(boxActor);
                if (mgr == 0) 
                {
                    if (EnableDebugLogging)
                        Console.WriteLine($"[ABILoot] Container 0x{boxActor:X}: No manager found");
                    return false;
                }

                // 2) Read InventoryContainerBaseList TArray at confirmed offset 0x140
                var baseList = ReadTArray(mgr + OFF_MGR_BASELIST);
                if (baseList.Count <= 0 || baseList.Data == 0)
                {
                    if (EnableDebugLogging)
                        Console.WriteLine($"[ABILoot] Container 0x{boxActor:X}: Empty or invalid BaseList");
                    return false;
                }

                // Validate first element has reasonable row/column
                ulong elem0 = baseList.Data;
                int row0 = DmaMemory.Read<int>(elem0 + OFF_INVBASE_ROW);
                int col0 = DmaMemory.Read<int>(elem0 + OFF_INVBASE_COLUMN);
                
                if (row0 < 0 || row0 > 64 || col0 < 0 || col0 > 64)
                {
                    if (EnableDebugLogging)
                        Console.WriteLine($"[ABILoot] Container 0x{boxActor:X}: Invalid row/col validation ({row0},{col0})");
                    return false;
                }

                if (EnableDebugLogging)
                    Console.WriteLine($"[ABILoot] Container 0x{boxActor:X}: BaseList has {baseList.Count} slots, first slot is ({row0},{col0})");

                // Dump first element structure to find the TArray
                if (EnableDebugLogging && baseList.Count > 0 && elem0 > 0x10000)
                {
                    Console.WriteLine($"[ABILoot]   Dumping first FInventoryContainerBase @ 0x{elem0:X}:");
                    
                    // Read first 0x60 bytes of the struct
                    byte[] structData = DmaMemory.ReadBytes(elem0, 0x60);
                    if (structData != null)
                    {
                        for (int offset = 0; offset < structData.Length; offset += 0x10)
                        {
                            // Read as TArray header (ptr + count + max)
                            if (offset + 0x10 <= structData.Length)
                            {
                                ulong ptr = BitConverter.ToUInt64(structData, offset + 0x00);
                                int arracount = BitConverter.ToInt32(structData, offset + 0x08);
                                int max = BitConverter.ToInt32(structData, offset + 0x0C);
                                
                                // Check if this looks like a valid TArray
                                bool looksLikeTArray = arracount >= 0 && arracount <= max && max < 1000;
                                if (arracount > 0)
                                    looksLikeTArray = looksLikeTArray && ptr != 0 && (ptr & 0xFFF0000000000000UL) == 0;
                                else
                                    looksLikeTArray = looksLikeTArray && (ptr == 0 || (ptr & 0xFFF0000000000000UL) == 0);
                                
                                string marker = looksLikeTArray ? " <-- Possible TArray" : "";
                                Console.WriteLine($"[ABILoot]     +0x{offset:X2}: Ptr=0x{ptr:X}, Count={arracount}, Max={max}{marker}");
                            }
                        }
                    }
                }

                int count = Math.Min(baseList.Count, 128);
                Vector3 contPos = ReadActorWorldPos(boxActor);
                int totalKids = 0;

                // Try the primary offset first (0x10 from dump)
                int childOffset = OFF_INVBASE_CHILD_ACTORS;

                // 4) Read all ChildActors arrays
                using var map = DmaMemory.Scatter();
                var r = map.AddRound(false);

                for (int i = 0; i < count; i++)
                {
                    ulong elem = baseList.Data + (ulong)(i * SIZEOF_INVBASE);
                    r[i].AddValueEntry<ulong>(0, elem + (ulong)childOffset + 0x00); // TArray Data
                    r[i].AddValueEntry<int>(1,   elem + (ulong)childOffset + 0x08); // TArray Count
                    r[i].AddValueEntry<int>(2,   elem + (ulong)childOffset + 0x0C); // TArray Max
                }
                map.Execute();

                // 5) Process each container slot
                for (int i = 0; i < count; i++)
                {
                    if (!r[i].TryGetValue(0, out ulong data) || data == 0) continue;
                    if (!r[i].TryGetValue(1, out int c) || c <= 0 || c > 512) continue;

                    var kids = DmaMemory.ReadArray<ulong>(data, c);
                    if (kids == null || kids.Length == 0) continue;

                    // Read class names for all child actors
                    using var map2 = DmaMemory.Scatter();
                    var r2 = map2.AddRound(false);
                    for (int k = 0; k < kids.Length; k++)
                    {
                        ulong a = kids[k]; 
                        if (a == 0) continue;
                        r2[k].AddValueEntry<uint>(0, a + 24); // FName
                    }
                    map2.Execute();

                    // Add items to output
                    int itemsInThisSlot = 0;
                    for (int k = 0; k < kids.Length; k++)
                    {
                        ulong a = kids[k]; 
                        if (a == 0) continue;
                        
                        string cls = r2[k].TryGetValue(0, out uint fname) ? ABINamePool.GetName(fname) : null;
                        
                        // ALWAYS log first few to see what we're dealing with
                        if (EnableDebugLogging && i == 0 && k < 5)
                            Console.WriteLine($"[ABILoot]       ChildActor[{k}] = 0x{a:X} ({cls ?? "NULL"})");
                        
                        if (string.IsNullOrEmpty(cls)) continue;
                        
                        if (!IsItem(cls)) 
                        {
                            if (EnableDebugLogging && i == 0 && k < 5)
                                Console.WriteLine($"[ABILoot]         ^ Filtered by IsItem()");
                            continue;
                        }

                        outList.Add(new Item {
                            Actor = a, 
                            ContainerActor = boxActor, 
                            InContainer = true,
                            ClassName = cls, 
                            Label = cls, 
                            Stack = 1, 
                            Position = contPos,
                            ApproxPrice = _priceProvider?.TryGetPrice(cls) ?? 0
                        });
                        itemsInThisSlot++;
                        totalKids++;
                    }
                    
                    if (EnableDebugLogging && i < 2)
                        Console.WriteLine($"[ABILoot]     Slot[{i}] @ offset 0x{childOffset:X}: {kids.Length} actors total, {itemsInThisSlot} recognized as items");
                }

                if (EnableDebugLogging && totalKids > 0)
                    Console.WriteLine($"[ABILoot] Container 0x{boxActor:X}: Found {totalKids} items");
                else if (EnableDebugLogging && count > 0 && totalKids == 0)
                    Console.WriteLine($"[ABILoot] Container 0x{boxActor:X}: {count} slots but no items found with any offset");

                return totalKids > 0;
            }
            catch (Exception ex) 
            { 
                if (EnableDebugLogging)
                    Console.WriteLine($"[ABILoot] ExpandContainer error for 0x{boxActor:X}: {ex.Message}");
                return false; 
            }
        }

        private static ulong GetContainerMgr(ulong boxActor)
        {
            for (int i = 0; i < CANDIDATE_MGR_OFFS.Length; i++)
            {
                ulong p = DmaMemory.Read<ulong>(boxActor + (ulong)CANDIDATE_MGR_OFFS[i]);
                // Valid user-space pointer
                if (p != 0 && (p & 0xFFF0000000000000UL) == 0 && p > 0x10000)
                    return p;
            }
            return 0;
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

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Public API for tracking searched containers
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public static void OnContainerSearched(ulong containerActor, List<Item> contents)
        {
            _searchedContainers[containerActor] = (DateTime.UtcNow.Ticks, new List<Item>(contents));
            if (EnableDebugLogging)
                Console.WriteLine($"[ABILoot] Container 0x{containerActor:X} searched, cached {contents.Count} items");
        }
    }
}