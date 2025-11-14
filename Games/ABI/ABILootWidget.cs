// File: Games/ABI/ABILootWidget.cs
// Real-time valuable loot display widget
// FIXED: Now movable and resizable, saves position/size to config

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace MamboDMA.Games.ABI
{
    internal static class ABILootWidget
    {
        private static bool _windowOpen = true;

        public static void Render(ABIGameConfig cfg, Vector3 localPos)
        {
            if (!cfg.DrawLootWidget) return;
            if (!ABILoot.TryGetLoot(out var frame) || frame.Items == null) return;

            // Filter and sort loot by price (descending), take top N
            var valuableItems = frame.Items
                .Where(item => item.ApproxPrice >= cfg.LootWidgetMinPrice)
                .OrderByDescending(item => item.ApproxPrice)
                .Take(cfg.LootWidgetMaxItems)
                .ToList();

            if (valuableItems.Count == 0) return;

            // Calculate distances and prepare data
            var itemData = new List<(ABILoot.Item item, float distance, string label)>();
            
            foreach (var item in valuableItems)
            {
                float distM = Vector3.Distance(localPos, item.Position) / 100f;
                
                string name = item.Label ?? item.ClassName ?? "Item";
                if (name.Length > 30) name = name.Substring(0, 27) + "...";
                
                itemData.Add((item, distM, name));
            }

            // Set initial position and size if not set
            if (cfg.LootWidgetPosition.X == 0 && cfg.LootWidgetPosition.Y == 0)
            {
                cfg.LootWidgetPosition = new Vector2(20, 400);
            }

            // Use ImGui window for movable/resizable widget
            ImGui.SetNextWindowPos(cfg.LootWidgetPosition, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(420, 450), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowBgAlpha(cfg.LootWidgetBackground.W);

            var windowFlags = ImGuiWindowFlags.NoCollapse;
            
            if (ImGui.Begin("? Valuable Loot", ref _windowOpen, windowFlags))
            {
                // Save position when moved
                var currentPos = ImGui.GetWindowPos();
                if (currentPos != cfg.LootWidgetPosition)
                {
                    cfg.LootWidgetPosition = currentPos;
                }

                // Header with item count and total value
                int totalValue = itemData.Sum(x => x.item.ApproxPrice);
                ImGui.TextColored(new Vector4(1f, 0.84f, 0f, 1f), $"Top {itemData.Count} Items - Total: ?{totalValue:N0}");
                ImGui.Separator();

                // Column headers
                ImGui.Columns(3, "loot_columns", true);
                ImGui.SetColumnWidth(0, 200);
                ImGui.SetColumnWidth(1, 100);
                ImGui.SetColumnWidth(2, 80);
                
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Item Name");
                ImGui.NextColumn();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Price");
                ImGui.NextColumn();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Distance");
                ImGui.NextColumn();
                
                ImGui.Separator();

                // Items list
                for (int i = 0; i < itemData.Count; i++)
                {
                    var (item, distM, name) = itemData[i];

                    // Color based on price and distance
                    Vector4 textColor;
                    if (item.ApproxPrice >= cfg.LootMinPriceImportant)
                        textColor = cfg.LootWidgetImportantColor;
                    else if (distM < 50f)
                        textColor = new Vector4(0.4f, 1f, 0.4f, 1f);
                    else
                        textColor = new Vector4(0.85f, 0.85f, 0.85f, 1f);

                    // Container indicator + name
                    string displayName = name;
                    if (item.InContainer)
                    {
                        displayName = "? " + name;
                    }
                    
                    ImGui.TextColored(textColor, displayName);
                    ImGui.NextColumn();

                    // Price
                    ImGui.TextColored(textColor, $"?{item.ApproxPrice:N0}");
                    ImGui.NextColumn();

                    // Distance
                    if (cfg.LootWidgetShowDistance)
                    {
                        ImGui.TextColored(textColor, $"{distM:F0}m");
                    }
                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
                ImGui.Separator();
                
                // Footer hint
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), 
                    "Drag to move ? Resize from corners ? Close to disable in settings");
            }
            ImGui.End();

            // Update config if window was closed
            if (!_windowOpen)
            {
                cfg.DrawLootWidget = false;
                _windowOpen = true; // Reset for next time
            }
        }
    }
}