using MamboDMA.Games.ABI;
using MamboDMA.Services;
using Svg.Model.Drawables.Elements;
using System.Numerics;
using static MamboDMA.Games.ABI.Players;

namespace MamboDMA.Games.CS2
{
    public static class CS2Players
    {
        public struct CS2Player
        {
            Vector3 Position;
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

            new Thread(CachePlayersLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "CS2.Players" }.Start();
        }

        public static void Stop() => _running = false;


        private static void CachePlayersLoop()
        {
            while (_running) { try { CachePlayers(); } catch { } HighResDelay(45); }
        }

        private static void CachePlayers()
        {

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
