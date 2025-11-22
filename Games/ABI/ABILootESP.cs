// File: Games/ABI/ABILootESP.cs
// FIXED: Applies origin bias to loot positions so they align with player positions
// This fixes the "loot underground" issue

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace MamboDMA.Games.ABI
{
    internal static class ABILootESP
    {
        private const int MAX_ITEMS_TO_DRAW = 300;
        private const int MAX_CONTAINERS_TO_DRAW = 100;
        
        private static int _estimatedVertexCount = 0;
        private const int VERTEX_BUDGET = 60000;

        public static void Render(ABIGameConfig cfg, Vector3 localPos, FMinimalViewInfo cam, float zoom)
        {
            if (!ABILoot.TryGetLoot(out var frame) || frame.Items == null) return;

            float W = ScreenService.Current.W;
            float H = ScreenService.Current.H;
            var dl = ImGui.GetBackgroundDrawList();

            _estimatedVertexCount = 0;

            // **CRITICAL FIX: Get the origin bias from Players**
            // Loot positions are raw world coordinates, we need to apply the same bias
            // that Players applies to make them align correctly
            Vector3 originBias = Players.GetOriginBias();

            var filtered = FilterAndPrioritizeItems(frame.Items, cfg, localPos, MAX_ITEMS_TO_DRAW);

            var groundItems = new List<ABILoot.Item>();
            var containerGroups = new Dictionary<ulong, List<ABILoot.Item>>();

            foreach (var item in filtered)
            {
                if (item.InContainer && item.ContainerActor != 0)
                {
                    if (!containerGroups.TryGetValue(item.ContainerActor, out var list))
                    {
                        list = new List<ABILoot.Item>();
                        containerGroups[item.ContainerActor] = list;
                    }
                    list.Add(item);
                }
                else groundItems.Add(item);
            }

            // Draw ground loot with bias correction
            if (cfg.DrawGroundLoot)
            {
                int drawn = 0;
                foreach (var item in groundItems)
                {
                    if (drawn++ >= MAX_ITEMS_TO_DRAW) break;
                    if (_estimatedVertexCount >= VERTEX_BUDGET) break;
                    
                    DrawLootItem(item, cfg, localPos, cam, zoom, dl, W, H, false, originBias);
                }
            }

            // Draw containers with bias correction
            if (cfg.DrawContainers && frame.Containers != null && _estimatedVertexCount < VERTEX_BUDGET)
            {
                var sortedContainers = frame.Containers
                    .Where(c => !c.IsEmpty || cfg.DrawEmptyContainers)
                    .OrderBy(c => Vector3.DistanceSquared(localPos, c.Position))
                    .Take(MAX_CONTAINERS_TO_DRAW)
                    .ToList();

                int drawn = 0;
                foreach (var container in sortedContainers)
                {
                    if (drawn++ >= MAX_CONTAINERS_TO_DRAW) break;
                    if (_estimatedVertexCount >= VERTEX_BUDGET) break;
                    
                    float distM = Vector3.Distance(localPos, container.Position) / 100f;
                    if (distM > cfg.ContainerMaxDistance) continue;

                    containerGroups.TryGetValue(container.Actor, out var items);
                    DrawContainer(container, items, cfg, localPos, cam, zoom, dl, W, H, originBias);
                }
            }
        }

        private static List<ABILoot.Item> FilterAndPrioritizeItems(List<ABILoot.Item> items, ABIGameConfig cfg, Vector3 localPos, int maxItems)
        {
            var includeTerms = ParseFilterTerms(cfg.LootFilterInclude);
            var excludeTerms = ParseFilterTerms(cfg.LootFilterExclude);

            var candidates = new List<(ABILoot.Item item, float distSq, int priority)>(items.Count);

            foreach (var item in items)
            {
                float distM = Vector3.Distance(localPos, item.Position) / 100f;
                float maxDist = item.InContainer ? cfg.ContainerMaxDistance : cfg.GroundLootMaxDistance;
                if (distM > maxDist) continue;

                if (item.ApproxPrice < cfg.LootMinPriceRegular)
                    continue;

                if (includeTerms.Count > 0)
                {
                    bool matches = false;
                    foreach (var term in includeTerms)
                        if ((item.ClassName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (item.Label?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                        { matches = true; break; }
                    if (!matches) continue;
                }

                if (excludeTerms.Count > 0)
                {
                    bool excluded = false;
                    foreach (var term in excludeTerms)
                        if ((item.ClassName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (item.Label?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                        { excluded = true; break; }
                    if (excluded) continue;
                }

                int priority = 0;
                if (item.ApproxPrice >= cfg.LootMinPriceImportant)
                    priority = 2;
                else if (distM < 50f)
                    priority = 1;

                candidates.Add((item, distM * distM, priority));
            }

            var result = candidates
                .OrderByDescending(x => x.priority)
                .ThenBy(x => x.distSq)
                .Take(maxItems)
                .Select(x => x.item)
                .ToList();

            return result;
        }

        private static void DrawLootItem(ABILoot.Item item, ABIGameConfig cfg, Vector3 localPos,
            FMinimalViewInfo cam, float zoom, ImDrawListPtr dl, float W, float H, bool isContainer, Vector3 originBias)
        {
            // **APPLY BIAS FIX: Add origin bias to loot position**
            Vector3 correctedPos = item.Position + originBias;
            
            if (!ABIMath.WorldToScreenZoom(correctedPos, cam, W, H, zoom, out var screen))
                return;

            float distM = Vector3.Distance(localPos, correctedPos) / 100f;

            bool isImportant = item.ApproxPrice >= cfg.LootMinPriceImportant;
            Vector4 color = isImportant ? cfg.LootImportantColor : cfg.LootRegularColor;

            uint col = ImGui.ColorConvertFloat4ToU32(color);
            float markerSize = isContainer ? cfg.ContainerMarkerSize : cfg.GroundLootMarkerSize;

            bool drawDetailed = distM < 100f;

            if (drawDetailed)
            {
                if (isImportant)
                {
                    DrawStar(dl, screen, markerSize, col);
                    _estimatedVertexCount += 24;
                }
                else
                {
                    DrawDiamond(dl, screen, markerSize, col);
                    _estimatedVertexCount += 20;
                }
            }
            else
            {
                dl.AddCircleFilled(screen, markerSize, col);
                _estimatedVertexCount += 24;
            }

            if (distM < 100f && (cfg.DrawGroundLootNames || (isContainer && cfg.DrawContainerNames)))
            {
                var label = item.Label ?? item.ClassName ?? "Item";
                
                if (label.Length > 30) label = label.Substring(0, 27) + "...";
                
                if (distM < 50f && cfg.LootShowPrice && item.ApproxPrice > 0)
                    label = $"{label} (?{item.ApproxPrice:N0})";
                
                if (cfg.DrawGroundLootDistance || (isContainer && cfg.DrawContainerDistance))
                    label = $"{label} [{distM:F0}m]";

                var textSize = ImGui.CalcTextSize(label);
                var textPos = new Vector2(screen.X - textSize.X / 2, screen.Y + markerSize + 4);

                dl.AddRectFilled(
                    new Vector2(textPos.X - 2, textPos.Y - 1),
                    new Vector2(textPos.X + textSize.X + 2, textPos.Y + textSize.Y + 1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.7f))
                );
                
                dl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(color), label);
                
                _estimatedVertexCount += 6 + (label.Length * 6);
            }
        }

        private static void DrawContainer(ABILoot.Container container, List<ABILoot.Item> items, ABIGameConfig cfg,
            Vector3 localPos, FMinimalViewInfo cam, float zoom, ImDrawListPtr dl, float W, float H, Vector3 originBias)
        {
            // **APPLY BIAS FIX: Add origin bias to container position**
            Vector3 correctedPos = container.Position + originBias;
            
            if (!ABIMath.WorldToScreenZoom(correctedPos, cam, W, H, zoom, out var screen))
                return;

            float distM = Vector3.Distance(localPos, correctedPos) / 100f;
            
            Vector4 color = container.IsEmpty ? cfg.ContainerEmptyColor : cfg.ContainerFilledColor;
            
            if (items != null && items.Any(x => x.ApproxPrice >= cfg.LootMinPriceImportant))
            {
                color = cfg.LootImportantColor;
            }
            
            uint col = ImGui.ColorConvertFloat4ToU32(color);

            float half = cfg.ContainerMarkerSize / 2;
            dl.AddRectFilled(
                new Vector2(screen.X - half, screen.Y - half),
                new Vector2(screen.X + half, screen.Y + half),
                col
            );
            dl.AddRect(
                new Vector2(screen.X - half, screen.Y - half),
                new Vector2(screen.X + half, screen.Y + half),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.9f)),
                0, ImDrawFlags.None, 2f
            );
            
            _estimatedVertexCount += 12;

            if (cfg.DrawContainerNames && distM < 150f)
            {
                int itemCount = items?.Count ?? container.ItemCount;
                string baseName = !string.IsNullOrWhiteSpace(container.Label) ? container.Label : "Container";
                
                if (baseName.Length > 20) baseName = baseName.Substring(0, 17) + "...";
                
                string label = container.IsEmpty ? $"{baseName} (Empty)" : $"{baseName} ({itemCount})";
                
                if (distM < 50f && container.ApproxPrice > 0)
                    label = $"{label} ?{container.ApproxPrice:N0}";
                
                if (cfg.DrawContainerDistance) label = $"{label} [{distM:F0}m]";

                var textSize = ImGui.CalcTextSize(label);
                var textPos = new Vector2(screen.X - textSize.X / 2, screen.Y + cfg.ContainerMarkerSize + 4);

                dl.AddRectFilled(
                    new Vector2(textPos.X - 2, textPos.Y - 1),
                    new Vector2(textPos.X + textSize.X + 2, textPos.Y + textSize.Y + 1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.7f))
                );
                dl.AddText(textPos, col, label);
                
                _estimatedVertexCount += 6 + (label.Length * 6);
            }
        }

        private static void DrawDiamond(ImDrawListPtr dl, Vector2 center, float size, uint color)
        {
            var points = new Vector2[]
            {
                new Vector2(center.X, center.Y - size),
                new Vector2(center.X + size, center.Y),
                new Vector2(center.X, center.Y + size),
                new Vector2(center.X - size, center.Y)
            };

            dl.AddConvexPolyFilled(ref points[0], points.Length, color);
            dl.AddPolyline(ref points[0], points.Length,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.9f)),
                ImDrawFlags.Closed, 1.5f);
        }

        private static void DrawStar(ImDrawListPtr dl, Vector2 center, float size, uint color)
        {
            var points = new Vector2[8];
            float outer = size;
            float inner = size * 0.4f;
            
            for (int i = 0; i < 8; i++)
            {
                float angle = (i * MathF.PI / 4f) - MathF.PI / 2f;
                float r = (i % 2 == 0) ? outer : inner;
                points[i] = new Vector2(
                    center.X + MathF.Cos(angle) * r,
                    center.Y + MathF.Sin(angle) * r
                );
            }

            dl.AddConvexPolyFilled(ref points[0], points.Length, color);
            dl.AddPolyline(ref points[0], points.Length,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.9f)),
                ImDrawFlags.Closed, 1.5f);
        }

        private static List<string> ParseFilterTerms(string filter)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(filter)) return result;

            var terms = filter.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var term in terms)
            {
                var t = term.Trim();
                if (!string.IsNullOrEmpty(t)) result.Add(t);
            }
            return result;
        }
    }
}