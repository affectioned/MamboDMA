using MamboDMA.Input;
using System.Numerics;
using System.Runtime.InteropServices;

namespace MamboDMA.Games.CS2
{
    public static class CS2Entities
    {
        public static ulong clientBase;
        public static ulong entityListPtr, localControllerBase;
        public static Matrix4x4 localViewMatrix;

        // https://github.com/neverlosecc/source2sdk/blob/cs2/sdk/include/source2sdk/client/LifeState_t.hpp
        public enum LifeState
        {
            LIFE_ALIVE = 256,
            LIFE_DYING = 0x1,
            LIFE_DEAD = 257,
            LIFE_RESPAWNABLE = 0x3,
            LIFE_RESPAWNING = 0x4,
        }

        public enum Team
        {
            Unknown = 0,
            Spectator = 1,
            Terrorists = 2,
            CounterTerrorists = 3
        }

        public struct CS2Entity
        {
            public LifeState LifeState;
            public int Health;
            public Team Team;
            public Vector3 Origin;
            public String Name;
        }

        private static readonly object Sync = new();
        private static List<CS2Entity> CachedEntities = new(64);
        public static CS2Entity LocalPlayer { get; private set; }

        public static IReadOnlyList<CS2Entity> GetCachedEntitiesSnapshot()
        {
            lock (Sync)
                return CachedEntities; // return the list itself as IReadOnlyList
        }

        private static List<DmaMemory.ModuleInfo> _modules = new();
        private static DmaMemory.ModuleInfo _clientModule;

        private static bool _running;
        public static void StartCache() => Start();
       
        private static void Start()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            CacheModules();

            CS2Offsets.ResolveOffsets(_clientModule);

            new Thread(CacheWorldLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "CS2.World" }.Start();
            new Thread(CacheEntitiesLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "CS2.Entities" }.Start();
        }

        public static void Stop() => _running = false;

        private static void CacheModules()
        {
            _modules = DmaMemory.GetModules();
            if (_modules == null) return;

            _clientModule = _modules.FirstOrDefault(m => string.Equals(m.Name, "client.dll", StringComparison.OrdinalIgnoreCase));
            if (_clientModule == null) return;
        }

        private static void CacheWorldLoop()
        {
            while (_running) { try { CacheWorld(); } catch { } HighResDelay(50); }
        }

        private static void CacheWorld()
        {
            clientBase = _clientModule.Base;

            entityListPtr = DmaMemory.Read<ulong>(clientBase + CS2Offsets.dwEntityList);

            localControllerBase = DmaMemory.Read<ulong>(clientBase + CS2Offsets.dwLocalPlayerController);
            ulong vmAddr = clientBase + CS2Offsets.dwViewMatrix;

            // 4x4 floats = 16 * 4 = 64 bytes
            Span<byte> vmBuf = stackalloc byte[64];

            if (DmaMemory.Read(vmAddr, vmBuf, VmmFlags.NOCACHE))
            {
                localViewMatrix = MemoryMarshal.Read<Matrix4x4>(vmBuf);
            }
        }

        private static void CacheEntitiesLoop()
        {
            while (_running) { try { CacheEntities(); } catch { } HighResDelay(45); }
        }

        private static void CacheEntities()
        {
            try
            {
                if (entityListPtr == 0)
                {
                    lock (Sync)
                    {
                        CachedEntities = [];
                        LocalPlayer = default;
                    }
                    return;
                }

                var entities = new List<CS2Entity>(64);
                CS2Entity localPlayer = default;
                bool localFound = false;

                for (int i = 0; i < 64; i++)
                {
                    if (!CS2EntityReader.TryGetControllerBase(i, entityListPtr, out var controllerBase))
                        continue;

                    bool isLocal = DmaMemory.Read<bool>(controllerBase + CS2Offsets.m_bIsLocalPlayerController);

                    if (!CS2EntityReader.TryGetPawnAddress(controllerBase, entityListPtr, out var pawnAddress))
                        continue;

                    var entity = CS2EntityReader.ReadEntityData(controllerBase, pawnAddress);

                    if (isLocal)
                    {
                        localPlayer = entity;
                        localFound = true;
                        continue;
                    }

                    entities.Add(entity);
                }

                lock (Sync)
                {
                    CachedEntities = entities;
                    if (localFound)
                        LocalPlayer = localPlayer;
                }
            }
            catch
            {
                // client closed / DMA error etc.
            }
        }

        private static void HighResDelay(int targetMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int sleepMs = Math.Max(0, targetMs - 1);
            if (sleepMs > 0) Thread.Sleep(sleepMs);
            while (sw.ElapsedMilliseconds < targetMs) Thread.SpinWait(80);
        }
    }
}
