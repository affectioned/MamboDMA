namespace MamboDMA.Games.DBD
{
    public static class DBDPlayers
    {
        public static ulong UWorld, UGameInstance, GameState, PersistentLevel;

        private static bool _running;

        private static List<DmaMemory.ModuleInfo> _modules = new();
        private static DmaMemory.ModuleInfo _baseModule;

        public static void StartCache()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            CacheModules();

            DBDOffsets.ResolveOffsets(_baseModule);

            new Thread(CacheWorldLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "DBD.World" }.Start();
        }

        private static void CacheModules()
        {
            _modules = DmaMemory.GetModules();
            if (_modules == null) return;

            _baseModule = _modules.FirstOrDefault(m => m.Base == DmaMemory.Base);
        }

        private static void CacheWorldLoop()
        {
            while (_running) { try { CacheWorld(); } catch { } HighResDelay(50); }
        }

        private static bool CacheWorld()
        {
            UWorld = DmaMemory.Read<ulong>(DmaMemory.Base + DBDOffsets.GWorld);
            if (UWorld == 0) return false;

            return true;
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
