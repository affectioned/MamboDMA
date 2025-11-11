using ImGuiNET;
using MamboDMA.Games.ABI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static MamboDMA.Games.ABI.Players;

namespace MamboDMA.Games.CS2
{
    public static class CS2ESP
    {
        public static void Render(bool drawLines, bool drawBoxes, bool drawNames, bool drawDistance, bool drawSkeletons, Vector4 colorPlayer, Vector4 colorBot,
            Vector4 colorBoxVisible, Vector4 colorBoxInvisible,
            Vector4 colorSkelVisible, Vector4 colorSkelInvisible, Vector4 colorLineVisible, Vector4 colorLineInvisible)
        {
            // TODO: add localPlayer view matrix
            var viewMatrix = new Matrix4x4();

            var list = ImGui.GetForegroundDrawList();
            var io = ImGui.GetIO();
            float scrW = io.DisplaySize.X, scrH = io.DisplaySize.Y;

            // Local helper uses the zoom-aware W2S
            bool W2S(in Vector3 ws, out Vector2 sp) =>
                CS2Math.WorldToScreen(ws, viewMatrix, scrW, scrH, out sp);

            var entityList = CS2Entities.GetCachedEntitiesSnapshot().ToArray();

            for (int i = 0; i < entityList.Length; i++)
            {
                var e = entityList[i];
                var lineColor = e.Team == CS2Entities.Team.Terrorists ? colorPlayer : colorBot;
                list.AddLine(new Vector2(), e.Origin, )
            }
        }

        private static void DrawBox(ImDrawListPtr list, Vector2 min, Vector2 max, uint color, float t)
        {
            float w = max.X - min.X;
            float h = max.Y - min.Y;
            float c = MathF.Min(20, MathF.Min(w * 0.25f, h * 0.25f));
            list.AddLine(min, new(min.X + c, min.Y), color, t);
            list.AddLine(min, new(min.X, min.Y + c), color, t);
            list.AddLine(new(max.X - c, min.Y), new(max.X, min.Y), color, t);
            list.AddLine(new(max.X, min.Y), new(max.X, min.Y + c), color, t);
            list.AddLine(new(min.X, max.Y - c), new(min.X, max.Y), color, t);
            list.AddLine(new(min.X, max.Y), new(min.X + c, max.Y), color, t);
            list.AddLine(new(max.X - c, max.Y), max, color, t);
            list.AddLine(max, new(max.X, max.Y - c), color, t);
        }
    }
}
