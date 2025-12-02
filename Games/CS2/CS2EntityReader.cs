using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static MamboDMA.Games.CS2.CS2Entities;

namespace MamboDMA.Games.CS2
{
    internal class CS2EntityReader
    {
        public static bool TryGetControllerBase(int index, ulong entityListPtr, out ulong controllerBase)
        {
            controllerBase = 0;

            var entryIndex = (index & 0x7FFF) >> 9;
            var listEntry = DmaMemory.Read<ulong>(entityListPtr + (ulong)(8 * entryIndex + 16));
            if (listEntry == 0)
                return false;

            controllerBase = DmaMemory.Read<ulong>(listEntry + (ulong)(112 * (index & 0x1FF)));
            return controllerBase != 0;
        }

        public static string ReadPlayerName(ulong controllerBase)
        {
            var buffer = DmaMemory.ReadBytes(controllerBase + CS2Offsets.m_iszPlayerName, 128);
            if (buffer == null)
                return string.Empty;

            var nullIndex = Array.IndexOf(buffer, (byte)0);
            if (nullIndex < 0) nullIndex = buffer.Length;

            return Encoding.UTF8.GetString(buffer, 0, nullIndex).Trim();
        }

        public static bool TryGetPawnAddress(ulong controllerBase, ulong entityListPtr, out ulong pawnAddress)
        {
            pawnAddress = 0;

            var playerPawn = DmaMemory.Read<ulong>(controllerBase + CS2Offsets.m_hPawn);
            if (playerPawn == 0)
                return false;

            var pawnIndex = (playerPawn & 0x7FFF) >> 9;
            var listEntry2 = DmaMemory.Read<ulong>(entityListPtr + (ulong)(8 * pawnIndex + 16));
            if (listEntry2 == 0)
                return false;

            pawnAddress = DmaMemory.Read<ulong>(listEntry2 + (ulong)(112 * (playerPawn & 0x1FF)));
            return pawnAddress != 0;
        }

        public static CS2Entity ReadEntityData(ulong controllerBase, ulong pawnAddress)
        {
            var lifeStateNum = DmaMemory.Read<int>(pawnAddress + CS2Offsets.m_lifeState);
            var health = DmaMemory.Read<int>(pawnAddress + CS2Offsets.m_iHealth);
            var teamNum = DmaMemory.Read<int>(pawnAddress + CS2Offsets.m_iTeamNum);
            var origin = DmaMemory.Read<Vector3>(pawnAddress + CS2Offsets.m_vOldOrigin);
            var name = ReadPlayerName(controllerBase);

            return new CS2Entity
            {
                LifeState = (LifeState)lifeStateNum,
                Health = health,
                Team = (Team)teamNum,
                Origin = origin,
                Name = name
            };
        }
    }
}
