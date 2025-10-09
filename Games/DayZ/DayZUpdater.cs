using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using MamboDMA.Services;

namespace MamboDMA.Games.DayZ
{
    /// <summary>
    /// Central offsets for DayZ (addresses, struct layouts).
    /// Keep this in sync with game patches.
    /// </summary>
    public static class DayZOffsets
    {
        // ─────────────────────────────
        // Modbase
        // ─────────────────────────────
        public const ulong World          = 0xF4B0A0; // Modbase::World
        public const ulong NetworkManager = 0xF5E1E0; // Modbase::Network
        public const ulong ScriptContext  = 0xF19398; // Modbase::ScriptContext
        public const ulong Tick           = 0xF19418; // Modbase::Tick

        // ─────────────────────────────
        // World
        // ─────────────────────────────
        public const ulong Camera             = 0x1B8;
        public const ulong LocalPlayer        = 0x2960;
        public const ulong LocalOffset        = 0x95E772;
        public const ulong BulletList         = 0xE00;
        public const ulong NoGrass            = 0xC00;

        public const ulong NearEntityList     = 0xF48;
        public const ulong FarEntityList      = 0x1090;
        public const ulong SlowEntityList     = 0x2010;

        public const ulong SlowEntSize        = 0x8;
        public const ulong SlowEntValidCount  = 0x1F90;
        public const ulong NearCount          = 0xEC0;
        public const ulong FarCount           = 0x1008;
        public const ulong ItemCount          = 0x1FDC;

        // ─────────────────────────────
        // Camera
        // ─────────────────────────────
        public const ulong ViewMatrix             = 0x8;
        public const ulong ProjectionMatrix       = 0xD0;
        public const ulong ViewProjection2        = 0xDC;
        public const ulong ViewPortMatrix         = 0x58;

        public const ulong InvertedViewUp         = 0x14;
        public const ulong InvertedViewForward    = 0x20;
        public const ulong InvertedViewTranslation= 0x2C;
        public const ulong InvertedViewRight      = 0x8;

        // ─────────────────────────────
        // Entity / Human / Player
        // ─────────────────────────────
        public const ulong Entity_Base = 0x180; // Base entity class

        public const ulong HumanType      = 0xA8;
        public const ulong VisualState    = 0x1C8;
        public const ulong LodShape       = 0x208;
        public const ulong Inventory      = 0x658;

        public const ulong TypeName       = Entity_Base + 0x70;  // HumanType::ObjectName
        public const ulong ModelName      = Entity_Base + 0x88;
        public const ulong ConfigName     = Entity_Base + 0xA8;  // HumanType::CategoryName
        public const ulong CleanName      = Entity_Base + 0x4F0;

        public const ulong Transform      = 0x1C8;
        public const ulong Position       = Transform + 0x2C;    // VisualState::Transform + 0x2C
        public const ulong InverseTransform= 0xA4;

        public const ulong IsDead         = 0xE2;

        // ─────────────────────────────
        // DayZPlayer / Infected
        // ─────────────────────────────
        public const ulong PlayerSkeleton         = 0x7E8;  // DayZPlayer::Skeleton
        public const ulong PlayerNetworkID        = 0x6E4;
        public const ulong PlayerNetworkClientPtr = 0x50;
        public const ulong PlayerInventory        = 0x658;
        public const ulong PlayerIsDead           = 0xE2;

        public const ulong InfectedSkeleton       = 0x678;  // DayZInfected::Skeleton

        // ─────────────────────────────
        // ScoreboardIdentity
        // ─────────────────────────────
        public const ulong Scoreboard_Name     = 0xF8;
        public const ulong Scoreboard_SteamID  = 0xA0;
        public const ulong Scoreboard_NetworkID= 0x30;

        // ─────────────────────────────
        // Inventory / Item / Cargo
        // ─────────────────────────────
        public const ulong ItemInventory   = 0x658;
        public const ulong CargoGrid       = 0x148;
        public const ulong CargoGridItems  = 0x38;
        public const ulong ItemQuality     = 0x194;

        // ─────────────────────────────
        // Weapon
        // ─────────────────────────────
        public const ulong WeaponIndex        = 0x6B0;
        public const ulong WeaponInfoTable    = 0x6B8;
        public const ulong WeaponInfoSize     = 0x6BC;
        public const ulong MuzzleCount        = 0x6C4;

        // ─────────────────────────────
        // Weapon Inventory / Magazines
        // ─────────────────────────────
        public const ulong WeaponInventoryMagazineRef = 0x150;
        public const ulong MagazineType               = 0x180;
        public const ulong MagazineAmmoCount          = 0x6B4;
        public const ulong MagazineBulletList         = 0xE00;
        public const ulong MagazineBulletList2        = 0x5A8;

        // ─────────────────────────────
        // Player Inventory
        // ─────────────────────────────
        public const ulong Hands      = 0xF8;
        public const ulong Clothing   = 0x150;
        public const ulong HandItemValid = 0x1CC;

        // ─────────────────────────────
        // Anim / Skeleton
        // ─────────────────────────────
        public const ulong AnimClass1       = 0x98;
        public const ulong AnimClass2       = 0x28;
        public const ulong AnimClassMatrixB = 0x54;
        public const ulong AnimClassMatrixArray = 0xBF0;
        public const ulong AnimClassComponent   = 0xB0;

        // ─────────────────────────────
        // ScriptContext
        // ─────────────────────────────
        public const ulong ConstantTable = 0x68;

        // ─────────────────────────────
        // Ammo
        // ─────────────────────────────
        public const ulong AmmoInitSpeed  = 0x364;
        public const ulong AmmoAirFriction= 0x3B4;
    }
    public static class DayZUpdater
    {
        private static CancellationTokenSource? _cts;

        private const string ExeName = "DayZ_x64.exe";
        private const ulong WorldOffset = 0xF4B0A0;   // Modbase::World
        private const ulong NetMgrOffset = 0xF612F0;  // Modbase::Network
        public static ulong NetMgrPtr => _netMgrAddr;
        public static ulong WorldPtr => _worldAddr;

        private static ulong _worldAddr;
        private static ulong _netMgrAddr;

        public static void Start()
        {
            _cts = new CancellationTokenSource();

            // Schedule loops
            JobSystem.Schedule(ct => WorldLoop(ct));
            JobSystem.Schedule(ct => CameraLoop(ct));
            JobSystem.Schedule(ct => EntityLoop(ct));
        }

        public static void Stop() => _cts?.Cancel();

        // ─────────────────────────────────────────────
        // World updater: attach + world base
        private static async Task WorldLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var isAttached = DmaMemory.IsAttached;
                ulong world = 0, netMgr = 0;
                int near = 0, far = 0, slow = 0, items = 0;

                if (isAttached)
                {
                    if (_worldAddr == 0 || _netMgrAddr == 0)
                    {
                        _worldAddr = DmaMemory.Base + DayZOffsets.World;
                        _netMgrAddr = DmaMemory.Base + DayZOffsets.NetworkManager;
                        Console.WriteLine($"[DayZUpdater] Base=0x{DmaMemory.Base:X} World@0x{_worldAddr:X} NetMgr@0x{_netMgrAddr:X}");
                    }

                    world = DmaMemory.Read<ulong>(_worldAddr);
                    netMgr = DmaMemory.Read<ulong>(_netMgrAddr);

                    if (world != 0)
                    {
                        near = DmaMemory.Read<int>(world + DayZOffsets.NearCount);
                        far  = DmaMemory.Read<int>(world + DayZOffsets.FarCount);
                        slow = DmaMemory.Read<int>(world + DayZOffsets.SlowEntValidCount);
                        items= DmaMemory.Read<int>(world + DayZOffsets.ItemCount);
                    }
                }

                // ✅ Only mark as attached when DMA is attached AND world != 0
                DayZSnapshots.Publish(new DayZSnapshot(
                    Attached: isAttached && world != 0,
                    World: world,
                    NetworkManager: netMgr,
                    NearCount: near,
                    FarCount: far,
                    SlowCount: slow,
                    ItemCount: items,
                    Players: 0,
                    Zombies: 0,
                    Cars: 0
                ));

                await Task.Delay(50, ct); // 20 Hz
            }
        }

        // ─────────────────────────────────────────────
        // Camera updater
        private static async Task CameraLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var snap = DayZSnapshots.Current;
                if (!snap.Attached || snap.World == 0) { await Task.Delay(50, ct); continue; }

                var camPtr = DmaMemory.Read<ulong>(snap.World + DayZOffsets.Camera); // World::Camera
                if (camPtr != 0)
                {
                    var cam = ReadCamera(camPtr);
                    DayZCameraSnapshots.Publish(cam);
                }

                await Task.Delay(16, ct); // ~60 Hz
            }
        }

        // ─────────────────────────────────────────────
        // Entity updater
        private static async Task EntityLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var snap = DayZSnapshots.Current;
                if (!snap.Attached || snap.World == 0) { await Task.Delay(100, ct); continue; }

                var ents = new List<Entity>();
                ents.AddRange(Entity.EnumerateEntities(snap.World, snap.NearCount, 0xF48));   // NearEntList
                ents.AddRange(Entity.EnumerateEntities(snap.World, snap.FarCount, 0x1090));   // FarEntList
                ents.AddRange(Entity.EnumerateEntities(snap.World, snap.SlowCount, 0x2010));  // SlowEntList

                EntitySnapshots.Publish(ents);

                await Task.Delay(100, ct); // 10 Hz
            }
        }

        // ─────────────────────────────────────────────
        // Camera type + helper
        public class DayZCamera
        {
            public Matrix4x4 ViewMatrix;
            public Matrix4x4 ProjectionMatrix;
            public Vector3 InvertedViewRight;
            public Vector3 InvertedViewUp;
            public Vector3 InvertedViewForward;
            public Vector3 InvertedViewTranslation;
        }

        public static DayZCamera ReadCamera(ulong cameraPtr)
        {
            var cam = new DayZCamera
            {
                ViewMatrix               = DmaMemory.Read<Matrix4x4>(cameraPtr + DayZOffsets.ViewMatrix),
                ProjectionMatrix         = DmaMemory.Read<Matrix4x4>(cameraPtr + DayZOffsets.ProjectionMatrix),
                InvertedViewUp           = DmaMemory.Read<Vector3>(cameraPtr + DayZOffsets.InvertedViewUp),
                InvertedViewForward      = DmaMemory.Read<Vector3>(cameraPtr + DayZOffsets.InvertedViewForward),
                InvertedViewTranslation  = DmaMemory.Read<Vector3>(cameraPtr + DayZOffsets.InvertedViewTranslation),
                InvertedViewRight        = DmaMemory.Read<Vector3>(cameraPtr + DayZOffsets.InvertedViewRight)

            };
            return cam;
        }

        public static bool WorldToScreenDayZ(DayZCamera cam, Vector3 worldPos, Vector2 screenSize, out Vector2 screenPos)
        {
            screenPos = Vector2.Zero;

            var temp = worldPos - cam.InvertedViewTranslation;

            float x = Vector3.Dot(temp, cam.InvertedViewRight);
            float y = Vector3.Dot(temp, cam.InvertedViewUp);
            float z = Vector3.Dot(temp, cam.InvertedViewForward);

            if (z < 0.65f) return false; // behind camera

            float normalizedX = (x / cam.ProjectionMatrix.M11) / z;
            float normalizedY = (y / cam.ProjectionMatrix.M22) / z;

            screenPos.X = (screenSize.X / 2f) + (normalizedX * (screenSize.X / 2f));
            screenPos.Y = (screenSize.Y / 2f) - (normalizedY * (screenSize.Y / 2f));

            return true;
        }

        // ─────────────────────────────────────────────
        // Snapshots
        public static class DayZCameraSnapshots
        {
            private static DayZCamera _current;
            public static DayZCamera Current => Volatile.Read(ref _current);
            public static void Publish(DayZCamera c) => Volatile.Write(ref _current, c);
        }

        public static class EntitySnapshots
        {
            private static List<Entity> _current = new();
            public static IReadOnlyList<Entity> Current => Volatile.Read(ref _current);
            public static void Publish(List<Entity> ents) => Volatile.Write(ref _current, ents);
        }

        // ─────────────────────────────────────────────
        // Entity class
        public sealed class Entity
        {
            public ulong Ptr;
            public string TypeName = "";
            public string ModelName = "";
            public string ConfigName = "";
            public string CleanName = "";
            public EntityType Category = EntityType.None;

            public Vector3 Position;
            public bool IsDead;

            public void Categorize()
            {
                if (ConfigName == "dayzplayer") { Category = EntityType.Player; return; }
                if (ConfigName == "dayzinfected") { Category = EntityType.Zombie; return; }
                if (ConfigName == "car") { Category = EntityType.Car; return; }
                if (ConfigName == "boat") { Category = EntityType.Boat; return; }
                if (ConfigName == "dayzanimal") { Category = EntityType.Animal; return; }

                if (ModelName.Contains("backpacks")) { Category = EntityType.Backpack; return; }
                if (ConfigName == "clothing") { Category = EntityType.Clothing; return; }
                if (ModelName.Contains("food")) { Category = EntityType.Food; return; }
                if (ModelName.Contains("ammunition")) { Category = EntityType.Ammo; return; }
                if (ModelName.Contains("firearms") || ConfigName == "Weapon") { Category = EntityType.Weapon; return; }
                if (ConfigName == "itemoptics") { Category = EntityType.Optics; return; }
                if (ModelName.Contains("camping")) { Category = EntityType.Base; return; }
                if (ModelName.Contains("melee")) { Category = EntityType.Melee; return; }
                if (ModelName.Contains("explosives")) { Category = EntityType.Explosives; return; }

                Category = EntityType.GroundItem;
            }

            public static List<Entity> EnumerateEntities(ulong world, int count, ulong listOffset)
            {
                var entities = new List<Entity>();
                if (world == 0 || count == 0) return entities;

                ulong listPtr = DmaMemory.Read<ulong>(world + listOffset);
                if (listPtr == 0) return entities;

                var ptrs = DmaMemory.ReadArray<ulong>(listPtr, count);
                if (ptrs == null || ptrs.Length == 0) return entities; // ✅ safety check

                foreach (var entPtr in ptrs)
                {
                    if (entPtr == 0) continue;

                    var ent = new Entity { Ptr = entPtr };

                    ent.TypeName   = Misc.ReadArmaString(entPtr + DayZOffsets.TypeName);
                    ent.ModelName  = Misc.ReadArmaString(entPtr + DayZOffsets.ModelName);
                    ent.ConfigName = Misc.ReadArmaString(entPtr + DayZOffsets.ConfigName);
                    ent.CleanName  = Misc.ReadArmaString(entPtr + DayZOffsets.CleanName);
                    
                    // Proper position read (VisualState → Transform → Translation)
                    ulong visualState = DmaMemory.Read<ulong>(entPtr + DayZOffsets.VisualState);
                    if (visualState != 0)
                    {
                        ulong transform = DmaMemory.Read<ulong>(visualState + DayZOffsets.Transform);
                        if (transform != 0)
                            ent.Position = DmaMemory.Read<Vector3>(transform + 0x2C);
                    }
                    
                    ent.IsDead = DmaMemory.Read<byte>(entPtr + DayZOffsets.IsDead) != 0;

                    ent.Categorize();
                    entities.Add(ent);
                }
                return entities;
            }
        }

        public enum EntityType
        {
            None,
            Player,
            Zombie,
            Car,
            Boat,
            Animal,
            Clothing,
            Weapon,
            Backpack,
            Food,
            Ammo,
            Rare,
            Optics,
            Base,
            Melee,
            Explosives,
            GroundItem
        }
    }
}
