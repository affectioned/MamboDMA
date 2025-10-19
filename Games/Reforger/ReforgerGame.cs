using System;
using System.Threading;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using MamboDMA.Games;
using ArmaReforgerFeeder;
using static MamboDMA.OverlayWindow;
using static MamboDMA.Misc;
using Raylib_cs;
using MamboDMA.Gui;
using MamboDMA.Services; // VmmService

namespace MamboDMA.Games.Reforger
{
    /// <summary>
    /// Hosts the Arma Reforger feeder inside MamboDMA and draws ESP (boxes/skeletons).
    /// </summary>
    public sealed class ReforgerGame : IGame
    {
        public string Name => "ArmaReforger";
        private bool _initialized;
        private bool _running;

        // Local ESP state (actors only)
        private static ActorDto[] _actors = Array.Empty<ActorDto>();
        private static GameObjects.VehicleDto[] _vehicles = Array.Empty<GameObjects.VehicleDto>();
        private static GameObjects.ItemDto[]    _items    = Array.Empty<GameObjects.ItemDto>();
        // Config accessor
        private static ReforgerConfig Cfg => Config<ReforgerConfig>.Settings;

        // Actor sink (called by Players.PublishLatestToUI via UiSink)
        public static void SetActors(ActorDto[] array, float w, float h)
        {
            // ignore w/h; we always use ScreenService.Current now
            Volatile.Write(ref _actors, array ?? Array.Empty<ActorDto>());
        }

        public void Initialize()
        {
            if (_initialized) return;

            // make sure screen service is initialized
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);

            Players.UiSink = SetActors;
            GameObjects.Start();
            _initialized = true;
        }

        public void Start()
        {
            if (_running) return;

            // Provide external accessors but DO NOT start workers unless attached.
            DmaMemory.AttachExternal(
                () => (DmaMemory.IsAttached, DmaMemory.Pid, DmaMemory.Base),
                addr => DmaMemory.Read(addr, out ulong v) ? (true, v) : (false, 0UL),
                (addr, size) => DmaMemory.ReadBytes(addr, (uint)size)
            );

            if (DmaMemory.IsAttached)
            {
                Players.StartWorkers();
                _running = true;
            }
            else
            {
                // Not attached yet; workers will be started when user clicks "Start Workers" after attaching.
                _running = false;
            }
        }

        public void Stop()
        {
            if (!_running) return;
            Players.StopWorkers();
            _running = false;
        }

        public void Tick()
        {
            // If not attached, skip any memory-driven logic to avoid crashes.
            if (!MamboDMA.DmaMemory.IsAttached)
                return;

            // Update camera first — always before projecting bones
            Game.UpdateCamera();

            var cfg = Cfg;
            Players.MaxDrawDistance = cfg.MaxDrawDistance;
            Players.FrameCap        = cfg.FrameCap;
            Players.FastIntervalMs  = cfg.FastIntervalMs;
            Players.HpIntervalMs    = cfg.HpIntervalMs;
            Players.SlowIntervalMs  = cfg.SlowIntervalMs;

            Players.IncludeFriendlies              = cfg.IncludeFriendlies;
            Players.OnlyPlayersFromPlayerManager   = cfg.OnlyPlayersFromManager;
            Players.RequireHitZones                = cfg.RequireHitZones;
            Players.IncludeRagdolls                = cfg.IncludeRagdolls;
            Players.AnimatedOnly                   = cfg.AnimatedOnly;

            Players.EnableSkeletons                = cfg.EnableSkeletons;
            Players.SkeletonLevel                  = (ArmaReforgerFeeder.Players.SkeletonDetail)cfg.SkeletonLevel;
            Players.SkeletonThickness              = cfg.SkeletonThickness;

            // Make sure bones use the current frame camera before projection
            Players.PublishLatestToUI();

            _vehicles = GameObjects.LatestVehicles ?? Array.Empty<GameObjects.VehicleDto>();
            _items    = GameObjects.LatestItems    ?? Array.Empty<GameObjects.ItemDto>();
        }

        public void Draw(ImGuiWindowFlags winFlags)
        {
            Config<ReforgerConfig>.DrawConfigPanel(Name, cfg =>
            {
                bool vmmReady = MamboDMA.DmaMemory.IsVmmReady;
                bool attached = MamboDMA.DmaMemory.IsAttached;

                // ───────────────────────────────
                // Quick VMM setup for Reforger (no process list)
                ImGui.TextDisabled("Quick Setup");
                if (!vmmReady)
                {
                    if (ImGui.Button("Init VMM"))
                    {
                        // Will set Snapshots.VmmReady=true on success
                        VmmService.InitOnly();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("← initialize before attaching");
                }
                else if (!attached)
                {
                    if (ImGui.Button("Attach (ArmaReforgerSteam.exe)"))
                    {
                        // Non-blocking attach; Snapshots will update on success,
                        // which GameSelector picks up to set its flags true next frame.
                        VmmService.Attach("ArmaReforgerSteam.exe");
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("← attaches without process picker");
                }

                // Status light
                var color = (vmmReady && attached) ? new Vector4(0, 0.8f, 0, 1) : new Vector4(1f, 0.3f, 0.2f, 1);
                DrawStatusInline(color, (vmmReady && attached) ? "Attached & Ready" : "Not attached");

                ImGui.Separator();

                // If not attached yet, show a hint and early out of the rest of the panel (prevents crashes).
                if (!attached)
                {
                    ImGui.TextDisabled("Attach to ArmaReforgerSteam.exe to enable ESP & workers.");
                    return;
                }

                // ───────────────────────────────
                if (ImGui.CollapsingHeader("General Filters"))
                {
                    ImGui.SliderFloat("Max Draw Distance (m)", ref cfg.MaxDrawDistance, 50f, 1000f);
                    ImGui.SliderInt("Frame Cap", ref cfg.FrameCap, 32, 1024);
                    ImGui.SliderInt("Fast loop (ms)", ref cfg.FastIntervalMs, 1, 16);
                    ImGui.SliderInt("HP loop (ms)", ref cfg.HpIntervalMs, 10, 100);
                    ImGui.SliderInt("Slow loop (ms)", ref cfg.SlowIntervalMs, 50, 500);

                    ImGui.Checkbox("Friendlies too", ref cfg.IncludeFriendlies);
                    ImGui.Checkbox("Only PlayerManager players", ref cfg.OnlyPlayersFromManager);
                    ImGui.Checkbox("Require Hitzones", ref cfg.RequireHitZones);
                    ImGui.Checkbox("Include ragdolls", ref cfg.IncludeRagdolls);
                    ImGui.Checkbox("Animated only (except dead)", ref cfg.AnimatedOnly);
                }

                // ───────────────────────────────
                if (ImGui.CollapsingHeader("ESP – Players"))
                {
                    ImGui.Checkbox("Draw boxes", ref cfg.DrawBoxes);
                    ImGui.SameLine(); ImGui.Checkbox("HP bar", ref cfg.HpBarEnabled);
                    ImGui.SameLine(); ImGui.Checkbox("HP text", ref cfg.HpTextEnabled);

                    ImGui.Checkbox("Show name", ref cfg.ShowName);
                    ImGui.SameLine(); ImGui.Checkbox("Show weapon", ref cfg.ShowWeapon);
                    ImGui.SameLine(); ImGui.Checkbox("Show distance", ref cfg.ShowDistance);

                    ImGui.SliderFloat("Box width offset (px)", ref cfg.BoxWidthOffsetPx, -100f, 200f, "%.0f");
                    ImGui.SliderFloat("Box height offset (px)", ref cfg.BoxHeightOffsetPx, -150f, 300f, "%.0f");
                    ImGui.SliderFloat("Box Y offset (px)", ref cfg.HeadTopOffsetPx, 1f, 40f, "%.1f");
                    ImGui.SliderFloat("Box outline thickness", ref cfg.BoxOutlineThick, 0.8f, 3.0f, "%.1f");
                    ImGui.SliderFloat("HP bar width (px)", ref cfg.HpBarWidthPx, 2f, 14f, "%.0f");

                    ImGui.Separator();
                    ImGui.Checkbox("Enable Skeletons", ref cfg.EnableSkeletons);
                    ImGui.SliderInt("Skeleton detail", ref cfg.SkeletonLevel, 6, 14);
                    ImGui.SliderFloat("Skeleton thickness", ref cfg.SkeletonThickness, 0.5f, 3.0f);
                }

                // ───────────────────────────────
                if (ImGui.CollapsingHeader("ESP – Items"))
                {
                    bool showWeapons = cfg.ShowItemsWeapons;
                    if (ImGui.Checkbox("Weapons", ref showWeapons))
                        cfg.ShowItemsWeapons = showWeapons;

                    bool showAmmo = cfg.ShowItemsAmmo;
                    if (ImGui.Checkbox("Ammo", ref showAmmo))
                        cfg.ShowItemsAmmo = showAmmo;

                    bool showAttachments = cfg.ShowItemsAttachments;
                    if (ImGui.Checkbox("Attachments", ref showAttachments))
                        cfg.ShowItemsAttachments = showAttachments;

                    bool showEquipment = cfg.ShowItemsEquipment;
                    if (ImGui.Checkbox("Equipment", ref showEquipment))
                        cfg.ShowItemsEquipment = showEquipment;

                    bool showMisc = cfg.ShowItemsMisc;
                    if (ImGui.Checkbox("Other / Misc", ref showMisc))
                        cfg.ShowItemsMisc = showMisc;
                }

                // ───────────────────────────────
                if (ImGui.CollapsingHeader("ESP – Vehicles"))
                {
                    bool showCars = cfg.ShowVehiclesCars;
                    ImGui.Checkbox("Cars & Trucks", ref showCars);
                    bool showHelis = cfg.ShowVehiclesHelis;
                    ImGui.Checkbox("Helicopters", ref showHelis);
                    bool showPlanes = cfg.ShowVehiclesPlanes;
                    ImGui.Checkbox("Planes", ref showPlanes);
                    bool showBoats = cfg.ShowVehiclesBoats;
                    ImGui.Checkbox("Boats", ref showBoats);
                }

                // ───────────────────────────────
                if (ImGui.CollapsingHeader("Colors"))
                {
                    ImGui.ColorEdit4("Box color", ref cfg.BoxColor);
                    ImGui.ColorEdit4("Box shadow", ref cfg.BoxShadowColor);
                    ImGui.ColorEdit4("Name color", ref cfg.NameColor);
                    ImGui.ColorEdit4("Weapon color", ref cfg.WeaponColor);
                    ImGui.ColorEdit4("Distance color", ref cfg.DistanceColor);
                    ImGui.ColorEdit4("HP text color", ref cfg.HpTextColor);
                    ImGui.ColorEdit4("Skeleton color", ref cfg.SkelColor);
                    ImGui.ColorEdit4("Skeleton shadow", ref cfg.SkelShadowColor);
                }

                // ───────────────────────────────
                ImGui.Separator();
                if (ImGui.Button(_running ? "Restart Workers" : "Start Workers"))
                {
                    if (!_running)
                    {
                        // Only start workers if attached
                        if (MamboDMA.DmaMemory.IsAttached)
                        {
                            Start();
                        }
                    }
                    else
                    {
                        Players.StopWorkers();
                        Players.StartWorkers();
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Stop Workers")) Stop();
            });

            // Only draw overlays when attached
            if (MamboDMA.DmaMemory.IsAttached)
            {
                DrawActorsOverlay();
                DrawVehiclesOverlay();
                DrawItemsOverlay();
            }
        }

        private static void DrawStatusInline(Vector4 color, string caption)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
            dl.AddCircleFilled(new Vector2(p.X + 5, y), 5, ImGui.ColorConvertFloat4ToU32(color));
            ImGui.Dummy(new Vector2(14, ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.TextDisabled(caption);
        }

        private static void DrawActorsOverlay()
        {
            var list = Volatile.Read(ref _actors);
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

                    // shadow
                    dl.AddRect(new Vector2(x - 1, y - 1), new Vector2(x + w + 1, y + h + 1),
                               colShadow, 0, ImDrawFlags.None, cfg.BoxOutlineThick + 1f);

                    // main
                    dl.AddRect(new Vector2(x, y), new Vector2(x + w, y + h),
                               colBox, 0, ImDrawFlags.None, cfg.BoxOutlineThick);

                    // HP bar
                    if (cfg.HpBarEnabled)
                    {
                        float hp01 = Math.Clamp(a.Health / 100f, 0f, 1f);
                        float bx = x + w + 4f;
                        float bh = h * hp01;
                        dl.AddRectFilled(new Vector2(bx - 1, y - 1),
                                         new Vector2(bx + cfg.HpBarWidthPx + 1, y + h + 1), colShadow);
                        uint hpCol = ImGui.GetColorU32(new Vector4(1f - hp01, hp01, 0f, 1f));
                        dl.AddRectFilled(new Vector2(bx, y + (h - bh)),
                                         new Vector2(bx + cfg.HpBarWidthPx, y + h), hpCol);

                        if (cfg.HpTextEnabled)
                            DrawLabel(dl, $"{Math.Clamp(a.Health, 0, 100)}%", cfg.HpTextColor, y - 16f, x, w, cfg.LabelShadow);
                    }

                    if (cfg.ShowName && !string.IsNullOrWhiteSpace(a.Name))
                        DrawLabel(dl, a.Name, cfg.NameColor, y - 18f, x, w, cfg.LabelShadow);
                    if (cfg.ShowWeapon && Players.TryGetEquippedWeapon(a.Ptr, out var gun))
                        DrawLabel(dl, gun, cfg.WeaponColor, y + h + 4f, x, w, cfg.LabelShadow);
                    if (cfg.ShowDistance)
                        DrawLabel(dl, $"{a.Distance}m", cfg.DistanceColor, y + h + 20f, x, w, cfg.LabelShadow);
                }
            }

            if (Players.EnableSkeletons)
            {
                uint colSkel = ImGui.GetColorU32(cfg.SkelColor);
                uint colSkelSh = ImGui.GetColorU32(cfg.SkelShadowColor);
                var edges = Players.ActiveSkelEdges;
                float thick = Math.Clamp(Players.SkeletonThickness, 0.5f, 5f);

                foreach (var a in list)
                {
                    if (!(a.HasBones && a.Bones != null)) continue;
                    for (int e = 0; e < edges.Length; e++)
                    {
                        var (A, B) = edges[e];
                        if (A < 0 || B < 0 || A >= a.Bones.Length || B >= a.Bones.Length) continue;
                        var pa = a.Bones[A];
                        var pb = a.Bones[B];
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
                // Category filters (based on config)
                if (v.Name.Contains("Truck", StringComparison.OrdinalIgnoreCase) && !cfg.ShowVehiclesCars) continue;
                if (v.Name.Contains("Car",   StringComparison.OrdinalIgnoreCase) && !cfg.ShowVehiclesCars) continue;
                if (v.Name.Contains("Heli",  StringComparison.OrdinalIgnoreCase) && !cfg.ShowVehiclesHelis) continue;
                if (v.Name.Contains("Plane", StringComparison.OrdinalIgnoreCase) && !cfg.ShowVehiclesPlanes) continue;
                if (v.Name.Contains("Boat",  StringComparison.OrdinalIgnoreCase) && !cfg.ShowVehiclesBoats) continue;

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
                    "Weapon"   => cfg.ShowItemsWeapons,
                    "Magazine" => cfg.ShowItemsAmmo,
                    "Item"     => cfg.ShowItemsMisc,
                    _          => false
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
