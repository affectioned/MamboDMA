using System;
using System.Runtime.InteropServices;
using System.Text;
using MamboDMA.Services;

namespace MamboDMA.Games.ABI
{
    public static class ABINamePool
    {
        private static readonly ulong GNames = DmaMemory.Base + ABIOffsets.GNames;
        private static byte _xorKey;

        public static string GetName(uint key)
        {
            try
            {
                if (_xorKey == 0)
                    _xorKey = DmaMemory.Read<byte>(DmaMemory.Base + ABIOffsets.DecryuptKey);

                uint chunk = key >> 16;
                ushort offset = (ushort)key;
                ulong poolChunk = DmaMemory.Read<ulong>(GNames + ((ulong)(chunk + 2) * 8));
                ulong entry = poolChunk + (ulong)(2 * offset);
                short header = DmaMemory.Read<short>(entry);
                int len = header >> 6;
                if (len <= 0 || len > 512) return string.Empty;

                byte[] buf = DmaMemory.ReadBytes(entry + 2, (uint)len);
                FNameDecrypt(buf, len);
                return Encoding.ASCII.GetString(buf);
            }
            catch { return string.Empty; }
        }

        private static void FNameDecrypt(byte[] input, int nameLength)
        {
            if (input == null || nameLength <= 0) return;
            if (nameLength > input.Length) nameLength = input.Length;

            byte key = _xorKey;

            for (int i = 0; i < nameLength; ++i)
            {
                byte dl = (byte)(((key >> 5) & 0x02) ^ key);
                byte cl = (byte)((((byte)(dl & 0x02) << 5)) ^ dl);
                byte al = (byte)((((cl >> 5) & 0x02) ^ input[i] ^ cl) ^ 0x39);
                input[i] = al;
            }
        }
    }
}
