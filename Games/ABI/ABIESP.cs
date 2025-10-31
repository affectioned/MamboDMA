using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace MamboDMA.Games.ABI
{
    public static class ABIESP
    {
        public static void Render(
            bool drawBoxes, bool drawNames, bool drawDistance, bool drawSkeletons,
            bool drawDeathMarkers, float deathMarkerMaxDist, float deathMarkerBaseSize,
            float maxDistMeters, float maxSkelDistMeters,
            Vector4 colorPlayer, Vector4 colorBot,
            Vector4 colorBoxVisible, Vector4 colorBoxInvisible,
            Vector4 colorSkelVisible, Vector4 colorSkelInvisible,
            Vector4 deadFill, Vector4 deadOutline)
        {
            if (!Players.TryGetFrame(out var fr)) return;
            var cam   = fr.Cam;
            var local = fr.Local;
            var positions = fr.Positions;
            if (positions == null || positions.Count == 0) return;

            List<Players.ABIPlayer> actors;
            lock (Players.Sync)
            {
                if (Players.ActorList.Count == 0) return;
                actors = new List<Players.ABIPlayer>(Players.ActorList);
            }

            var posMap = new Dictionary<ulong, Players.ActorPos>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
                posMap[positions[i].Pawn] = positions[i];

            var list = ImGui.GetForegroundDrawList();
            var io = ImGui.GetIO();
            float scrW = io.DisplaySize.X, scrH = io.DisplaySize.Y;

            for (int i = 0; i < actors.Count; i++)
            {
                if (!posMap.TryGetValue(actors[i].Pawn, out var ap)) continue;
                if (IsBogusPos(ap.Position)) continue;
                float distCm = Vector3.Distance(local, ap.Position);
                float distM  = distCm / 100f;
                if (distM > maxDistMeters) continue;

                if (!ABIMath.WorldToScreen(ap.Position, cam, scrW, scrH, out var screen)) continue;

                if (ap.IsDead)
                {
                    if (drawDeathMarkers && distM <= deathMarkerMaxDist)
                    {
                        DrawDiamond(list, screen, distM, deathMarkerBaseSize,
                                    ImGui.GetColorU32(deadFill), ImGui.GetColorU32(deadOutline));
                        if (drawDistance)
                            list.AddText(new Vector2(screen.X - 14, screen.Y + (deathMarkerBaseSize + 4)), 0xFFFFFFFF, $"{distM:F1} m");
                    }
                    continue;
                }

                bool isVis = ap.IsVisible;
                uint clrName = ImGui.GetColorU32(actors[i].IsBot ? colorBot : colorPlayer);
                uint clrBox  = ImGui.GetColorU32(isVis ? colorBoxVisible : colorBoxInvisible);
                uint clrSkel = ImGui.GetColorU32(isVis ? colorSkelVisible : colorSkelInvisible);

                Vector2 min2, max2;

                if (Players.TryGetSkeleton(actors[i].Pawn, out var bones) && bones != null && bones.Length >= 14)
                {
                    var headWS = bones[Skeleton.IDX_Head];
                    var footL  = bones[Skeleton.IDX_Foot_L];
                    var footR  = bones[Skeleton.IDX_Foot_R];
                    var feetWS = new Vector3((footL.X + footR.X) * 0.5f, (footL.Y + footR.Y) * 0.5f, (footL.Z + footR.Z) * 0.5f);

                    Vector2? head2D = null, feet2D = null;
                    if (ABIMath.WorldToScreen(headWS, cam, scrW, scrH, out var headScr)) head2D = headScr;
                    if (ABIMath.WorldToScreen(feetWS, cam, scrW, scrH, out var feetScr)) feet2D = feetScr;

                    if (head2D.HasValue && feet2D.HasValue)
                    {
                        float h = MathF.Abs(head2D.Value.Y - feet2D.Value.Y);
                        h = Math.Clamp(h, 20f, 800f);
                        float w = h * 0.35f;
                        float cy = (head2D.Value.Y + feet2D.Value.Y) * 0.5f;
                        min2 = new Vector2(screen.X - w * 0.5f, cy - h * 0.5f);
                        max2 = new Vector2(screen.X + w * 0.5f, cy + h * 0.5f);
                    }
                    else
                    {
                        float bh = Math.Clamp(150f / MathF.Max(distM, 3f), 60f, 250f);
                        float bw = bh * 0.35f;
                        min2 = new(screen.X - bw / 2, screen.Y - bh / 2);
                        max2 = new(screen.X + bw / 2, screen.Y + bh / 2);
                    }

                    if (drawBoxes) DrawBox(list, min2, max2, clrBox, 1.5f);
                    if (drawNames) list.AddText(new Vector2((min2.X + max2.X) * 0.5f - 18, min2.Y - 18), clrName, actors[i].IsBot ? "BOT" : "PMC");
                    if (drawDistance) list.AddText(new Vector2((min2.X + max2.X) * 0.5f - 12, max2.Y + 4), 0xFFFFFFFF, $"{distM:F1} m");

                    if (ap.HealthMax > 1f)
                        DrawHealthBar(list, new Vector2(min2.X, min2.Y - 8f), max2.X - min2.X, ap.Health, ap.HealthMax);

                    if (drawSkeletons && distM <= maxSkelDistMeters)
                        Skeleton.Draw(list, bones, cam, scrW, scrH, clrSkel);
                }
                else
                {
                    float bh = Math.Clamp(150f / MathF.Max(distM, 3f), 60f, 250f);
                    float bw = bh * 0.35f;
                    min2 = new(screen.X - bw / 2, screen.Y - bh / 2);
                    max2 = new(screen.X + bw / 2, screen.Y + bh / 2);

                    if (drawBoxes) DrawBox(list, min2, max2, clrBox, 1.5f);
                    if (drawNames) list.AddText(new Vector2((min2.X + max2.X) * 0.5f - 18, min2.Y - 18), clrName, actors[i].IsBot ? "BOT" : "PMC");
                    if (drawDistance) list.AddText(new Vector2((min2.X + max2.X) * 0.5f - 12, max2.Y + 4), 0xFFFFFFFF, $"{distM:F1} m");

                    if (ap.HealthMax > 1f)
                        DrawHealthBar(list, new Vector2(min2.X, min2.Y - 8f), max2.X - min2.X, ap.Health, ap.HealthMax);
                }
            }
        }

        public static bool IsBogusPos(in Vector3 p)
        {
            const float ex = 0.5f, ez = 1.0f;
            return MathF.Abs(p.X) <= ex && MathF.Abs(p.Y) <= ex && MathF.Abs(p.Z + 90f) <= ez;
        }

        private static void DrawDiamond(ImDrawListPtr list, Vector2 center, float distM, float baseSizePx, uint fill, uint outline)
        {
            float sz = Math.Clamp(baseSizePx * (120f / MathF.Max(distM, 8f)), baseSizePx * 0.4f, baseSizePx * 1.2f);
            Vector2 p0 = new(center.X, center.Y - sz);
            Vector2 p1 = new(center.X + sz, center.Y);
            Vector2 p2 = new(center.X, center.Y + sz);
            Vector2 p3 = new(center.X - sz, center.Y);

            list.AddQuadFilled(p0, p1, p2, p3, fill);
            float t = MathF.Max(1.2f, baseSizePx * 0.16f);
            list.AddLine(p0, p1, outline, t);
            list.AddLine(p1, p2, outline, t);
            list.AddLine(p2, p3, outline, t);
            list.AddLine(p3, p0, outline, t);
        }

        private static void DrawHealthBar(ImDrawListPtr list, Vector2 topLeft, float width, float health, float maxHealth)
        {
            float h = 5f;
            float pct = Math.Clamp(maxHealth > 0f ? (health / maxHealth) : 0f, 0f, 1f);

            var bgMin = topLeft;
            var bgMax = new Vector2(topLeft.X + width, topLeft.Y + h);

            uint bgCol = ImGui.GetColorU32(new Vector4(0.15f, 0f, 0f, 0.85f));
            list.AddRectFilled(bgMin, bgMax, bgCol, 2f);

            float fillW = width * pct;
            if (fillW > 0.5f)
            {
                var flMax = new Vector2(topLeft.X + fillW, topLeft.Y + h);
                uint fillCol = ImGui.GetColorU32(new Vector4(0.15f, 0.9f, 0.15f, 0.95f));
                list.AddRectFilled(bgMin, flMax, fillCol, 2f);
            }

            uint outline = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));
            list.AddRect(bgMin, bgMax, outline, 2f, ImDrawFlags.None, 1.0f);
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
