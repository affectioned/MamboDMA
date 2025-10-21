using MamboDMA.Games.ABI;
using System.Numerics;
using static MamboDMA.Games.ABI.Players;

namespace MamboDMA.Games.CS2
{
    public static class Players
    {
        public static ulong clientBase, entityListPtr, controllerEntry, playerController, pawnEntry, playerPawn;
        public static uint entityPawn, entityHealth;
        public struct CS2Player
        {
            public int Index;
            public ulong Controller;
            public ulong Pawn;
            public uint Health;
            public Vector3 Position;
        }

        private static readonly List<CS2Player> CachedPlayers = new();

        private static List<DmaMemory.ModuleInfo> _modules = new();
        private static DmaMemory.ModuleInfo _clientModule;

        private static bool _running;
        public static void StartCache() => Start();
        public static void Start()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            new Thread(CacheWorldLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "CS2.World" }.Start();
            new Thread(CachePlayersLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "CS2.Players" }.Start();
        }

        public static void Stop() => _running = false;

        private static void CacheWorldLoop()
        {
            while (_running) { try { CacheWorld(); } catch { } HighResDelay(50); }
        }

        private static bool CacheWorld()
        {
            _modules = DmaMemory.GetModules();
            if( _modules == null ) return false;

            _clientModule = _modules.FirstOrDefault(m => string.Equals(m.Name, "client.dll", StringComparison.OrdinalIgnoreCase));
            if (_clientModule == null) return false;

            clientBase = _clientModule.Base;

            entityListPtr = DmaMemory.Read<ulong>(clientBase + CS2Offsets.dwEntityList);
            if (entityListPtr == 0) return false;

            return true;
        }

        private static void CachePlayersLoop()
        {
            while (_running) { try { CachePlayers(); } catch { } HighResDelay(45); }
        }

        private static void CachePlayers()
        {
            if (entityListPtr == 0) return;

            ulong controllerEntry = DmaMemory.Read<ulong>(entityListPtr + 0x10);
            if (controllerEntry == 0) return;

            lock (CachedPlayers)
            {
                CachedPlayers.Clear();

                for (int i = 1; i <= 64; i++)
                {
                    ulong playerController = DmaMemory.Read<ulong>(controllerEntry + (ulong)(120 * i));
                    if (playerController == 0) continue;

                    uint pawn = DmaMemory.Read<uint>(playerController + CS2Offsets.m_hPawn);
                    if ((pawn & 0x7FFFu) == 0) continue;

                    ulong pawnEntry = DmaMemory.Read<ulong>(
                        entityListPtr + 0x8UL * (ulong)((pawn & 0x7FFFu) >> 9) + 0x10UL);

                    ulong playerPawn = DmaMemory.Read<ulong>(pawnEntry + (ulong)(120 * (pawn & 0x1FFu)));
                    if (playerPawn == 0) continue;

                    uint health = DmaMemory.Read<uint>(playerPawn + CS2Offsets.m_iHealth);
                    Vector3 position = DmaMemory.Read<Vector3>(playerPawn + CS2Offsets.m_vOldOrigin);

                    CachedPlayers.Add(new CS2Player
                    {
                        Index = i,
                        Controller = playerController,
                        Pawn = playerPawn,
                        Health = health,
                        Position = position
                    });
                }
            }
        }

        private static void HighResDelay(int targetMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int sleepMs = Math.Max(0, targetMs - 1);
            if (sleepMs > 0) Thread.Sleep(sleepMs);
            while (sw.ElapsedMilliseconds < targetMs) Thread.SpinWait(80);
        }

        public static IReadOnlyList<CS2Player> GetCachedPlayers()
        {
            lock (CachedPlayers)
            {
                return CachedPlayers.ToList();
            }
        }
    }
}
