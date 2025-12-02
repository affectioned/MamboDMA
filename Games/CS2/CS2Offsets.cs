using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MamboDMA.Games.CS2
{
    public static class CS2Offsets
    {
        // namespace CS2Dumper.Offsets 
        // Module: client.dll
        public const ulong dwEntityList = 0x1D11D78;
        public const ulong dwLocalPlayerController = 0x1E1BC58;
        public const ulong dwViewMatrix = 0x1E30450;

        // namespace CS2Dumper.Schemas
        // Module: client.dll
        // Class count: 490
        // Enum count: 8
        // public static class ClientDll
        // public static class CBasePlayerController 
        public const ulong m_hPawn = 0x6B4; // CHandle<C_BasePlayerPawn>

        // public static class CBasePlayerController
        public const ulong m_iszPlayerName = 0x6E8; // char[128]

        // public static class C_BaseEntity
        public const ulong m_lifeState = 0x354; // uint8

        // public static class C_BaseEntity
        public const ulong m_iHealth = 0x34C; // int32

        // public static class C_BaseEntity
        public const ulong m_iTeamNum = 0x3EB; // uint8

        // public static class C_BasePlayerPawn
        public const ulong m_vOldOrigin = 0x15A0; // Vector

    }
}
