using MamboDMA.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static MamboDMA.Games.ABI.Players;

namespace MamboDMA.Games.CS2
{
    public static class CS2Entities
    {
        public static ulong clientBase;
        public static ulong entityListPtr, listEntry, controllerBase, playerPawn, listEntry2, addressBase;

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

        public static readonly object Sync = new();

        private static List<CS2Entity> CachedEntities = new();

        private static List<DmaMemory.ModuleInfo> _modules = new();
        private static DmaMemory.ModuleInfo _clientModule;

        private static bool _running;
        public static void StartCache() => Start();
       
        private static void Start()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            new Thread(CacheWorldLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "CS2.World" }.Start();
            new Thread(CacheEntitiesLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "CS2.Entities" }.Start();
        }

        public static void Stop() => _running = false;

        private static void CacheWorldLoop()
        {
            while (_running) { try { CacheWorld(); } catch { } HighResDelay(50); }
        }

        private static bool CacheWorld()
        {
            _modules = DmaMemory.GetModules();
            if (_modules == null) return false;

            _clientModule = _modules.FirstOrDefault(m => string.Equals(m.Name, "client.dll", StringComparison.OrdinalIgnoreCase));
            if (_clientModule == null) return false;

            clientBase = _clientModule.Base;

            entityListPtr = DmaMemory.Read<ulong>(clientBase + CS2Offsets.dwEntityList);
            if (entityListPtr == 0) return false;

            return true;
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
                    lock (Sync) CachedEntities = [];
                    return;
                }

                var tmp = new List<CS2Entity>(64);

                for (int i = 0; i < 64; i++)
                {
                    var entryIndex = (i & 0x7FFF) >> 9;

                    listEntry = DmaMemory.Read<ulong>(entityListPtr + (ulong)(8 * entryIndex + 16));
                    if (listEntry == 0) continue;

                    controllerBase = DmaMemory.Read<ulong>(listEntry + (ulong)(112 * (i & 0x1FF)));
                    if (controllerBase == 0) continue;

                    // we can get name from the controller
                    var buffer = DmaMemory.ReadBytes(controllerBase + CS2Offsets.m_iszPlayerName, 128);
                    if (buffer == null) continue;

                    var nullIndex = Array.IndexOf(buffer, (byte)0);
                    if (nullIndex < 0) nullIndex = buffer.Length;
                    var name = Encoding.UTF8.GetString(buffer, 0, nullIndex).Trim();

                    playerPawn = DmaMemory.Read<ulong>(controllerBase + CS2Offsets.m_hPawn);
                    if (playerPawn == 0) continue;

                    var pawnIndex = (playerPawn & 0x7FFF) >> 9;

                    listEntry2 = DmaMemory.Read<ulong>(entityListPtr + 0x8 * pawnIndex + 16);
                    if (listEntry2 == 0) continue;

                    addressBase = DmaMemory.Read<ulong>(listEntry2 + 112 * (playerPawn & 0x1FF));
                    if (addressBase == 0) continue;

                    var lifeStateNum = DmaMemory.Read<int>(addressBase + CS2Offsets.m_lifeState);
                    var lifeState = (LifeState)lifeStateNum;

                    var health = DmaMemory.Read<int>(addressBase + CS2Offsets.m_iHealth);

                    var teamNum = DmaMemory.Read<int>(addressBase + CS2Offsets.m_iTeamNum);
                    var team = (Team)teamNum;

                    var origin = DmaMemory.Read<Vector3>(addressBase + CS2Offsets.m_vOldOrigin);

                    tmp.Add(new CS2Entity { LifeState = lifeState, Health = health, Team = team, Origin = origin, Name = name });
                }

                lock (Sync)
                    CachedEntities = tmp;
            }
            catch (Exception ex)
            {
                Logger.Error($"[CacheEntities] Exception: {ex.Message}\n{ex.StackTrace}");
                lock (Sync) CachedEntities = [];
            }
        }

        private static void HighResDelay(int targetMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int sleepMs = Math.Max(0, targetMs - 1);
            if (sleepMs > 0) Thread.Sleep(sleepMs);
            while (sw.ElapsedMilliseconds < targetMs) Thread.SpinWait(80);
        }

        public static List<CS2Entity> GetCachedEntitiesSnapshot()
        {
            lock (Sync) return [.. CachedEntities];
        }
    }
}
