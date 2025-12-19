using MamboDMA.Games.ABI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MamboDMA.Games.DBD
{
    public static class DBDNamePool
    {
        private static readonly ulong GNames = DmaMemory.Base + DBDOffsets.GNames;

        public static string GetName(uint actorId)
        {
            try
            {
                // actorId layout: high 16 = table, low 16 = row
                uint tableIndex = actorId >> 16;
                ushort rowIndex = (ushort)actorId;

                ulong chunkBase = DmaMemory.Read<ulong>(GNames + 16 + (ulong)(tableIndex) * 8);

                // 4 bytes per entry index
                ulong entry = chunkBase + (ulong)(4 * rowIndex);

                short header = DmaMemory.Read<short>(entry);
                int len = header >> 6;
                if (len <= 0 || len > 512) return string.Empty;

                byte[] buf = DmaMemory.ReadBytes(entry + 2, (uint)len);
                return Encoding.ASCII.GetString(buf);
            }
            catch { return string.Empty; }
        }
    }
}
