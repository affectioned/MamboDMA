using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MamboDMA.Games.CS2
{
    public static class CS2Offsets
    {
        public const ulong dwEntityList = 0x1D00690;
        public const ulong m_hPawn = 0x6B4;
        public const ulong m_iHealth = 0x34C; // int32
        public const ulong m_vOldOrigin = 0x15A0; // Vector
    }
}
