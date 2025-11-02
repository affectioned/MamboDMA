using MamboDMA.Games.ABI;
using MamboDMA.Services;
using System.Numerics;
using static MamboDMA.Games.ABI.Players;

namespace MamboDMA.Games.CS2
{
    public static class Players
    {
        public static ulong clientBase;
        public static ulong entityListPtr, listEntry, currentController, pawnHandle, listEntry2, currentPawn;
        
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

            // first entry to the entity list
            listEntry = DmaMemory.Read<ulong>(entityListPtr + 0x10);
            if (listEntry == 0) return;

            for (int i = 0; i < 64; i++)
            {
                // get current controller
                currentController = DmaMemory.Read<ulong>(listEntry + (ulong)(i * 0x78));
                if (currentController == 0) continue;

                // get pawn handle
                pawnHandle = DmaMemory.Read<ulong>(currentController + CS2Offsets.m_hPlayerPawn);
                if (pawnHandle == 0) continue;

                // second entry, now we find the pawn
                // Find which part ("chunk") of the entity list our pawn is stored in.
                // Every chunk holds 512 entities, and each chunk address is 8 bytes apart.
                //
                // (pawnHandle & 0x7FFF) -> gets the entity number from the handle
                // >> 9 -> divides that number by 512 to find which chunk it’s in
                // * 0x8 -> moves 8 bytes per chunk (because each address is 8 bytes)
                // + 0x10 -> skips a small header at the start of the list
                //
                // The result is the memory address of that chunk.
                listEntry2 = DmaMemory.Read<ulong>(entityListPtr + 0x8 * ((pawnHandle & 0x7FFF) >> 9) + 0x10);
                if (listEntry2 == 0) continue;

                // Get the actual pawn pointer inside the chunk we found earlier.
                // Each chunk holds up to 512 entities, and each entity entry is 0x78 bytes apart.
                //
                // (pawnHandle & 0x1FF) -> gets the position of our entity inside the chunk (like index % 512)
                // * 0x78 -> moves forward by 0x78 bytes for each entity slot
                //
                // The result is the memory address of the specific pawn.
                currentPawn = DmaMemory.Read<ulong>(listEntry2 + 0x78 * (pawnHandle & 0x1FF));
                if (currentPawn == 0) continue;

                // get pawn attributes
                int pawnHealth = DmaMemory.Read<int>(currentPawn + CS2Offsets.m_iHealth);

                // get controller attributes
                char controllerPlayerName = DmaMemory.Read<char>(currentController + CS2Offsets.m_iszPlayerName);

                Logger.Info($"[CS2] Player: {controllerPlayerName}, Health: {pawnHealth}");
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
