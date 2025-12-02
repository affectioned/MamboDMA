using ImGuiNET;
using MamboDMA.Games.ABI;
using System.Collections.Generic;
using System.Numerics;

namespace MamboDMA.Games.CS2
{
    public static class CS2ESP
    {
        public static void Render(
            bool drawBoxes, bool drawNames, bool drawSkeletons,
            Vector4 colorPlayer, Vector4 colorBot,
            Vector4 colorBoxVisible, Vector4 colorBoxInvisible,
            Vector4 colorSkelVisible, Vector4 colorSkelInvisible)
        {
            var list = ImGui.GetForegroundDrawList();
            var io = ImGui.GetIO();
            float scrW = io.DisplaySize.X, scrH = io.DisplaySize.Y;

            Matrix4x4 viewMatrix = new Matrix4x4(); // or however you read it

            // one-liner W2S wrapper
            bool W2S(in Vector3 ws, out Vector2 sp) =>
                CS2Math.WorldToScreen(ws, viewMatrix, scrW, scrH, out sp);

            // color conversion once so we don’t spam calls
            uint colPlayer = ImGui.GetColorU32(colorPlayer);
            uint colBot = ImGui.GetColorU32(colorBot);
            uint colBoxVis = ImGui.GetColorU32(colorBoxVisible);
            uint colBoxInvis = ImGui.GetColorU32(colorBoxInvisible);
            uint colSkelVis = ImGui.GetColorU32(colorSkelVisible);
            uint colSkelInvis = ImGui.GetColorU32(colorSkelInvisible);

            // TODO: actually read local player and its team + origin
            viewMatrix = CS2Entities.localViewMatrix;
            var local = CS2Entities.LocalPlayer; // whatever you have
            CS2Entities.Team localTeam = local.Team;
            Vector3 localPos = local.Origin;

            var entityList = CS2Entities.GetCachedEntitiesSnapshot();

            for (int i = 0; i < entityList.Count; i++)
            {
                var e = entityList[i];

                if (e.LifeState != CS2Entities.LifeState.LIFE_ALIVE) continue;
                if (e.Health <= 0) continue;
                if (e.Team == localTeam) continue;

                // world positions we need
                Vector3 feetWorld = e.Origin;
                Vector3 headWorld = e.Origin + new Vector3(0, 0, 72f);

                // project feet
                if (!W2S(feetWorld, out var feetScreen))
                    continue;

                // project head
                if (!W2S(headWorld, out var headScreen))
                    continue;

                // basic vis flag – replace with your real visibility check
                bool isVisible = false; // or your own logic

                // height + width from projection
                float height = feetScreen.Y - headScreen.Y;
                if (height <= 0) continue;

                float width = height * 0.45f;
                float halfW = width * 0.5f;

                Vector2 boxMin = new Vector2(feetScreen.X - halfW, headScreen.Y);
                Vector2 boxMax = new Vector2(feetScreen.X + halfW, feetScreen.Y);

                if (drawBoxes)
                {
                    uint colBox = isVisible ? colBoxVis : colBoxInvis;
                    list.AddRect(boxMin, boxMax, colBox, 0f, ImDrawFlags.None, 1.5f);
                }

                if (drawNames)
                {
                    string name = e.Name.Length > 0 ? e.Name : "player";
                    var textSize = ImGui.CalcTextSize(name);

                    // 2px above box
                    Vector2 namePos = new Vector2(
                        feetScreen.X - textSize.X * 0.5f,
                        headScreen.Y - textSize.Y - 2f
                    );
                    list.AddText(namePos, colPlayer, name);
                }
            }
        }
    }
}
