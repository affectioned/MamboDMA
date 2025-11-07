using System;
using System.Numerics;
using System.Linq;
using ImGuiNET;
using MamboDMA.Games;
using ArmaReforgerFeeder;
using static MamboDMA.Misc;
using MamboDMA.Services;

namespace MamboDMA.Games.Reforger
{
    public sealed class ReforgerGame : IGame
    {
        public string Name => "ArmaReforger";
        private bool _initialized;
        private bool _running;

        private static ActorDto[] _actors = Array.Empty<ActorDto>();
        private static GameObjects.VehicleDto[] _vehicles = Array.Empty<GameObjects.VehicleDto>();
        private static GameObjects.ItemDto[] _items = Array.Empty<GameObjects.ItemDto>();
        private static ReforgerConfig Cfg => Config<ReforgerConfig>.Settings;

        public static void SetActors(ActorDto[] array, float w, float h)
        {
            System.Threading.Volatile.Write(ref _actors, array ?? Array.Empty<ActorDto>());
        }

        public void Initialize()
        {
            if (_initialized) return;
            
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);

            Players.UiSink = SetActors;
            _initialized = true;
        }

        public void Start()
        {
            if (_running) return;
            
            DmaMemory.AttachExternal(
                () => (DmaMemory.IsAttached, DmaMemory.Pid, DmaMemory.Base),
                addr => DmaMemory.Read(addr, out ulong v) ? (true, v) : (false, 0UL),
                (addr, size) => DmaMemory.ReadBytes(addr, (uint)size)
            );

            if (DmaMemory.IsAttached)
            {
                Players.StartWorkers();
                GameObjects.Start();
                _running = true;
            }
            else
            {
                _running = false;
            }
        }

        public void Stop()
        {
            if (!_running) return;
            
            Players.StopWorkers();
            GameObjects.Stop();
            _running = false;
        }

        public void Tick()
        {
            if (!DmaMemory.IsAttached) return;

            Game.UpdateCamera();

            // Sync ALL config to runtime
            var cfg = Cfg;
            
            // Players
            Players.MaxDrawDistance = cfg.MaxDrawDistance;
            Players.FrameCap = cfg.FrameCap;
            Players.FastIntervalMs = cfg.FastIntervalMs;
            Players.HpIntervalMs = cfg.HpIntervalMs;
            Players.SlowIntervalMs = cfg.SlowIntervalMs;
            Players.IncludeFriendlies = cfg.IncludeFriendlies;
            Players.OnlyPlayersFromPlayerManager = cfg.OnlyPlayersFromManager;
            Players.RequireHitZones = cfg.RequireHitZones;
            Players.IncludeRagdolls = cfg.IncludeRagdolls;
            Players.AnimatedOnly = cfg.AnimatedOnly;
            Players.EnableSkeletons = cfg.EnableSkeletons;
            Players.SkeletonLevel = (Players.SkeletonDetail)cfg.SkeletonLevel;
            Players.SkeletonThickness = cfg.SkeletonThickness;

            // GameObjects - CRITICAL: Sync Hz settings!
            GameObjects.MaxDrawDistance = cfg.MaxDrawDistance;
            GameObjects.VehiclesHz = cfg.VehiclesHz;  // This was missing!
            GameObjects.ItemsHz = cfg.ItemsHz;        // This was missing!

            Players.PublishLatestToUI();

            _vehicles = GameObjects.LatestVehicles ?? Array.Empty<GameObjects.VehicleDto>();
            _items = GameObjects.LatestItems ?? Array.Empty<GameObjects.ItemDto>();
        }

        public void Draw(ImGuiWindowFlags winFlags)
        {
            if (UiVisibility.MenusHidden) return;
            
            Config<ReforgerConfig>.DrawConfigPanel(Name, cfg =>
            {
                DrawConnectionStatus();
                ImGui.Separator();

                if (!DmaMemory.IsAttached)
                {
                    ImGui.TextColored(new Vector4(1f, 0.3f, 0.2f, 1f), "Not attached to game process");
                    ImGui.TextDisabled("Attach to ArmaReforgerSteam.exe to enable ESP & features.");
                    return;
                }

                if (ImGui.BeginTabBar("ReforgerTabs"))
                {
                    if (ImGui.BeginTabItem("Players"))
                    {
                        DrawPlayersTab(cfg);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Vehicles"))
                    {
                        DrawVehiclesTab(cfg);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Items"))
                    {
                        DrawItemsTab(cfg);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Performance"))
                    {
                        DrawPerformanceTab(cfg);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Debug"))
                    {
                        DrawDebugTab(cfg);
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.Separator();
                DrawWorkerControls();
            });

            if (DmaMemory.IsAttached && !UiVisibility.MenusHidden)
            {
                DrawActorsOverlay();
                DrawVehiclesOverlay();
                DrawItemsOverlay();
            }
        }

        private void DrawConnectionStatus()
        {
            bool vmmReady = DmaMemory.IsVmmReady;
            bool attached = DmaMemory.IsAttached;

            ImGui.TextDisabled("Connection Status");
            
            if (!vmmReady)
            {
                if (ImGui.Button("Initialize VMM")) VmmService.InitOnly();
                ImGui.SameLine();
                ImGui.TextDisabled("← Click to initialize memory interface");
            }
            else if (!attached)
            {
                if (ImGui.Button("Attach to ArmaReforgerSteam.exe"))
                    VmmService.Attach("ArmaReforgerSteam.exe");
                ImGui.SameLine();
                ImGui.TextDisabled("← Quick attach without process picker");
            }

            var statusColor = (vmmReady && attached) 
                ? new Vector4(0, 0.8f, 0, 1) 
                : new Vector4(1f, 0.3f, 0.2f, 1);
            
            string statusText = (vmmReady && attached) 
                ? $"Connected (PID: {DmaMemory.Pid}, Base: 0x{DmaMemory.Base:X})" 
                : "Not connected";
            
            DrawStatusIndicator(statusColor, statusText);
        }

        private void DrawPlayersTab(ReforgerConfig cfg)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "ESP Settings");
            
            ImGui.Checkbox("Draw boxes", ref cfg.DrawBoxes);
            ImGui.SameLine();
            ImGui.Checkbox("HP bar", ref cfg.HpBarEnabled);
            ImGui.SameLine();
            ImGui.Checkbox("HP text", ref cfg.HpTextEnabled);

            ImGui.Checkbox("Show name", ref cfg.ShowName);
            ImGui.SameLine();
            ImGui.Checkbox("Show weapon", ref cfg.ShowWeapon);
            ImGui.SameLine();
            ImGui.Checkbox("Show distance", ref cfg.ShowDistance);

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Box Adjustments");
            
            ImGui.SliderFloat("Box width offset", ref cfg.BoxWidthOffsetPx, -100f, 200f, "%.0f px");
            ImGui.SliderFloat("Box height offset", ref cfg.BoxHeightOffsetPx, -150f, 300f, "%.0f px");
            ImGui.SliderFloat("Head Y offset", ref cfg.HeadTopOffsetPx, 1f, 40f, "%.1f px");
            ImGui.SliderFloat("Outline thickness", ref cfg.BoxOutlineThick, 0.8f, 3.0f, "%.1f");
            ImGui.SliderFloat("HP bar width", ref cfg.HpBarWidthPx, 2f, 14f, "%.0f px");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Skeleton ESP");
            
            ImGui.Checkbox("Enable skeletons", ref cfg.EnableSkeletons);
            
            if (cfg.EnableSkeletons)
            {
                ImGui.SliderInt("Skeleton detail", ref cfg.SkeletonLevel, 6, 14);
                ImGui.SameLine();
                ImGui.TextDisabled(cfg.SkeletonLevel switch 
                { 
                    6 => "(Compact)", 
                    10 => "(Lite)", 
                    14 => "(Full)", 
                    _ => "" 
                });
                
                ImGui.SliderFloat("Skeleton thickness", ref cfg.SkeletonThickness, 0.5f, 3.0f, "%.1f");
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Filters");
            
            ImGui.Checkbox("Include friendlies", ref cfg.IncludeFriendlies);
            ImGui.Checkbox("Only PlayerManager players", ref cfg.OnlyPlayersFromManager);
            ImGui.Checkbox("Require hitzones", ref cfg.RequireHitZones);
            ImGui.Checkbox("Include ragdolls", ref cfg.IncludeRagdolls);
            ImGui.Checkbox("Animated only (except dead)", ref cfg.AnimatedOnly);
        }

        private void DrawVehiclesTab(ReforgerConfig cfg)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Vehicle Categories");
            
            ImGui.Checkbox("Cars & Trucks", ref cfg.ShowVehiclesCars);
            ImGui.Checkbox("Helicopters", ref cfg.ShowVehiclesHelis);
            ImGui.Checkbox("Planes", ref cfg.ShowVehiclesPlanes);
            ImGui.Checkbox("Boats & Ships", ref cfg.ShowVehiclesBoats);

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Performance");
            
            ImGui.SliderInt("Update rate (Hz)", ref cfg.VehiclesHz, 1, 60);
            ImGui.TextDisabled($"Scanning {_vehicles.Length} vehicles");
        }

        private void DrawItemsTab(ReforgerConfig cfg)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Item Categories");
            
            ImGui.Checkbox("Weapons", ref cfg.ShowItemsWeapons);
            ImGui.Checkbox("Ammunition & Magazines", ref cfg.ShowItemsAmmo);
            ImGui.Checkbox("Weapon Attachments", ref cfg.ShowItemsAttachments);
            ImGui.Checkbox("Equipment & Gear", ref cfg.ShowItemsEquipment);
            ImGui.Checkbox("Other / Misc Items", ref cfg.ShowItemsMisc);

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Performance");
            
            ImGui.SliderInt("Update rate (Hz)", ref cfg.ItemsHz, 1, 30);
            ImGui.TextDisabled($"Scanning {_items.Length} items");
        }

        private void DrawPerformanceTab(ReforgerConfig cfg)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Draw Distance");
            ImGui.SliderFloat("Max distance", ref cfg.MaxDrawDistance, 50f, 2000f, "%.0f m");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Thread Timing (ms)");
            
            ImGui.SliderInt("Fast loop (players)", ref cfg.FastIntervalMs, 1, 16);
            ImGui.SameLine();
            ImGui.TextDisabled("← Lower = smoother ESP");
            
            ImGui.SliderInt("HP loop", ref cfg.HpIntervalMs, 10, 100);
            ImGui.SliderInt("Slow loop (meta)", ref cfg.SlowIntervalMs, 50, 500);

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Limits");
            
            ImGui.SliderInt("Player frame cap", ref cfg.FrameCap, 32, 512);
            ImGui.TextDisabled($"Currently rendering {_actors.Length} players");
        }

        private void DrawDebugTab(ReforgerConfig cfg)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Diagnostics");
            
            if (ImGui.Button("Dump Entity Lists"))
            {
                if (DmaMemory.Read(DmaMemory.Base + Off.Game, out ulong game) && game != 0 &&
                    DmaMemory.Read(game + Off.GameWorld, out ulong gw) && gw != 0)
                {
                    EntityListManager.DebugPrintAllLists(gw);
                }
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Reset Entity Cache"))
            {
                EntityListManager.ResetCache();
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Statistics:");
            ImGui.BulletText($"Players: {_actors.Length}");
            ImGui.BulletText($"Vehicles: {_vehicles.Length}");
            ImGui.BulletText($"Items: {_items.Length}");
            ImGui.BulletText($"Camera FOV: {Game.Camera.Fov:F1}°");
            ImGui.BulletText($"Camera Pos: ({Game.Camera.Position.X:F1}, {Game.Camera.Position.Y:F1}, {Game.Camera.Position.Z:F1})");
            ImGui.BulletText($"Screen: {Game.Screen.W}x{Game.Screen.H}");
        }

        private void DrawWorkerControls()
        {
            if (ImGui.Button(_running ? "Restart Workers" : "Start Workers"))
            {
                if (_running)
                {
                    Players.StopWorkers();
                    GameObjects.Stop();
                    System.Threading.Thread.Sleep(100);
                    Players.StartWorkers();
                    GameObjects.Start();
                }
                else
                {
                    if (DmaMemory.IsAttached) Start();
                }
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Stop Workers"))
            {
                Stop();
            }
            
            ImGui.SameLine();
            ImGui.TextDisabled(_running ? "Workers running" : "Workers stopped");
        }

        private static void DrawStatusIndicator(Vector4 color, string caption)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
            
            dl.AddCircleFilled(new Vector2(p.X + 5, y), 5, ImGui.ColorConvertFloat4ToU32(color));
            
            ImGui.Dummy(new Vector2(14, ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.Text(caption);
        }

        private static void DrawActorsOverlay()
        {
            var list = System.Threading.Volatile.Read(ref _actors);
            if (list == null || list.Length == 0) return;
            var dl = ImGui.GetForegroundDrawList();
            var cfg = Cfg;

            if (cfg.DrawBoxes)
            {
                uint colBox = ImGui.GetColorU32(cfg.BoxColor);
                uint colShadow = ImGui.GetColorU32(cfg.BoxShadowColor);

                foreach (var a in list)
                {
                    if (a.Bones == null || a.Bones.Length == 0) continue;
                    float minX = a.Bones.Min(b => b.X);
                    float maxX = a.Bones.Max(b => b.X);
                    float minY = a.Bones.Min(b => b.Y);
                    float maxY = a.Bones.Max(b => b.Y);

                    float x = minX + cfg.BoxWidthOffsetPx;
                    float y = minY - cfg.HeadTopOffsetPx;
                    float w = (maxX - minX);
                    float h = (maxY - minY) + cfg.BoxHeightOffsetPx;

                    dl.AddRect(new Vector2(x - 1, y - 1), new Vector2(x + w + 1, y + h + 1), colShadow, 0, ImDrawFlags.None, cfg.BoxOutlineThick + 1f);
                    dl.AddRect(new Vector2(x, y), new Vector2(x + w, y + h), colBox, 0, ImDrawFlags.None, cfg.BoxOutlineThick);

                    if (cfg.HpBarEnabled)
                    {
                        float hp01 = Math.Clamp(a.Health / 100f, 0f, 1f);
                        float bx = x + w + 4f;
                        float bh = h * hp01;
                        dl.AddRectFilled(new Vector2(bx - 1, y - 1), new Vector2(bx + cfg.HpBarWidthPx + 1, y + h + 1), colShadow);
                        uint hpCol = ImGui.GetColorU32(new Vector4(1f - hp01, hp01, 0f, 1f));
                        dl.AddRectFilled(new Vector2(bx, y + (h - bh)), new Vector2(bx + cfg.HpBarWidthPx, y + h), hpCol);
                    }

                    if (cfg.ShowName && !string.IsNullOrWhiteSpace(a.Name)) DrawLabel(dl, a.Name, cfg.NameColor, y - 18f, x, w, cfg.LabelShadow);
                    if (cfg.ShowWeapon && Players.TryGetEquippedWeapon(a.Ptr, out var gun)) DrawLabel(dl, gun, cfg.WeaponColor, y + h + 4f, x, w, cfg.LabelShadow);
                    if (cfg.ShowDistance) DrawLabel(dl, $"{a.Distance}m", cfg.DistanceColor, y + h + 20f, x, w, cfg.LabelShadow);
                }
            }

            if (Players.EnableSkeletons)
            {
                uint colSkel = ImGui.GetColorU32(Cfg.SkelColor);
                uint colSkelSh = ImGui.GetColorU32(Cfg.SkelShadowColor);
                var edges = Players.ActiveSkelEdges;
                float thick = Math.Clamp(Players.SkeletonThickness, 0.5f, 5f);

                foreach (var a in list)
                {
                    if (!(a.HasBones && a.Bones != null)) continue;
                    for (int e = 0; e < edges.Length; e++)
                    {
                        var (A, B) = edges[e];
                        if (A < 0 || B < 0 || A >= a.Bones.Length || B >= a.Bones.Length) continue;
                        var pa = a.Bones[A]; var pb = a.Bones[B];
                        if (float.IsNaN(pa.X) || float.IsNaN(pa.Y) || float.IsNaN(pb.X) || float.IsNaN(pb.Y)) continue;
                        dl.AddLine(new Vector2(pa.X + 1, pa.Y + 1), new Vector2(pb.X + 1, pb.Y + 1), colSkelSh, thick + 1f);
                        dl.AddLine(new Vector2(pa.X, pa.Y), new Vector2(pb.X, pb.Y), colSkel, thick);
                    }
                }
            }
        }

        private static void DrawVehiclesOverlay()
        {
            var list = _vehicles;
            if (list == null || list.Length == 0) return;
            
            var dl = ImGui.GetForegroundDrawList();
            var cfg = Cfg;

            foreach (var v in list)
            {
                // Filter by user settings
                bool show = true;
                if (v.Name.Contains("Truck", StringComparison.OrdinalIgnoreCase) || 
                    v.Name.Contains("Car", StringComparison.OrdinalIgnoreCase))
                    show = cfg.ShowVehiclesCars;
                else if (v.Name.Contains("Heli", StringComparison.OrdinalIgnoreCase))
                    show = cfg.ShowVehiclesHelis;
                else if (v.Name.Contains("Plane", StringComparison.OrdinalIgnoreCase))
                    show = cfg.ShowVehiclesPlanes;
                else if (v.Name.Contains("Boat", StringComparison.OrdinalIgnoreCase))
                    show = cfg.ShowVehiclesBoats;

                if (!show) continue;

                if (!Game.WorldToScreen(v.Position, out float sx, out float sy)) continue;
                
                string caption = $"{v.Name} [{v.Distance}m]";
                uint col = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 1f, 1f));
                dl.AddText(new Vector2(sx, sy), col, caption);
            }
        }

        private static void DrawItemsOverlay()
        {
            var list = _items;
            if (list == null || list.Length == 0) return;
            
            var dl = ImGui.GetForegroundDrawList();
            var cfg = Cfg;

            foreach (var it in list)
            {
                bool show = it.Kind switch
                {
                    "Weapon" => cfg.ShowItemsWeapons,
                    "Magazine" => cfg.ShowItemsAmmo,
                    "Item" => cfg.ShowItemsMisc,
                    _ => false
                };
                
                if (!show) continue;
                if (!Game.WorldToScreen(it.Position, out float sx, out float sy)) continue;
                
                string caption = $"{it.Name} [{it.Distance}m]";
                uint col = ImGui.GetColorU32(new Vector4(1f, 1f, 0f, 1f));
                dl.AddText(new Vector2(sx, sy), col, caption);
            }
        }

        private static void DrawLabel(ImDrawListPtr dl, string text, Vector4 color, float y, float x, float w, Vector4 shadow)
        {
            var sz = ImGui.CalcTextSize(text);
            float tx = x + (w - sz.X) * 0.5f;
            uint fg = ImGui.GetColorU32(color);
            uint sh = ImGui.GetColorU32(shadow);
            dl.AddText(new Vector2(tx + 1, y + 1), sh, text);
            dl.AddText(new Vector2(tx, y), fg, text);
        }
    }
}