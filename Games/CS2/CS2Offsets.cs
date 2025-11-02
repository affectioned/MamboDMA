using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MamboDMA.Games.CS2
{
    public static class CS2Offsets
    {
        // Module: client.dll
        // namespace CS2Dumper.Offsets 
        public const ulong dwEntityList = 0x1D0C9F8;

        // namespace CS2Dumper.Schemas {
        // Module: client.dll
        // Class count: 490
        // Enum count: 8
        // public static class ClientDll
        // Parent: C_BaseEntity
        // public static class CCSPlayerController
        public const ulong m_hPlayerPawn = 0x8FC; // CHandle<C_CSPlayerPawn>
        // public static class C_BaseEntity
        public const ulong m_iHealth = 0x34C; // int32
        // public static class CBasePlayerController
        public const ulong m_iszPlayerName = 0x6E8; // char[128]
    }
}
