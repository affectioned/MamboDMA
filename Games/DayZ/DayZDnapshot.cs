namespace MamboDMA.Games.DayZ
{
    public static class DayZSnapshots
    {
        private static DayZSnapshot _current = DayZSnapshot.Empty;

        public static DayZSnapshot Current => Volatile.Read(ref _current);

        public static void Publish(DayZSnapshot s) => Volatile.Write(ref _current, s);

        public static void Mutate(Func<DayZSnapshot, DayZSnapshot> mutate)
            => Publish(mutate(Current));
    }    
    public record DayZSnapshot(
        bool Attached,
        ulong World,
        ulong NetworkManager,
        int NearCount,
        int FarCount,
        int SlowCount,
        int ItemCount,
        int Players,
        int Zombies,
        int Cars
    )
    {
        public static readonly DayZSnapshot Empty = new(
            Attached: false,
            World: 0,
            NetworkManager: 0,
            NearCount: 0,
            FarCount: 0,
            SlowCount: 0,
            ItemCount: 0,
            Players: 0,
            Zombies: 0,
            Cars: 0
        );
    }
}
