using System.Numerics;
using ImGuiNET;
using MamboDMA.Services;
using static MamboDMA.Misc;
using static MamboDMA.OverlayWindow;
using static MamboDMA.Games.DayZ.DayZUpdater;

namespace MamboDMA.Games.DayZ
{
    public sealed class DayZGame : IGame
    {
        public string Name => "DayZ";

        private bool _initialized;
        private bool _running;

        private static DayZConfig Cfg => Config<DayZConfig>.Settings;

        public void Initialize()
        {
            if (_initialized) return;

            // initialize screen service from current monitor if needed
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);

            _initialized = true;
        }

        public void Start()
        {
            if (_running) return;
            DayZUpdater.Start();
            _running = true;
        }

        public void Stop()
        {
            if (!_running) return;
            DayZUpdater.Stop();
            _running = false;
        }

        public void Tick() { }

        public void Draw(ImGuiWindowFlags winFlags)
        {
            Config<DayZConfig>.DrawConfigPanel(Name, cfg =>
            {
                var snap = DayZSnapshots.Current;
                var ready = MamboDMA.DmaMemory.IsVmmReady && MamboDMA.DmaMemory.IsAttached;
                var color = ready ? new Vector4(0, 0.8f, 0, 1) : new Vector4(1f, 0.3f, 0.2f, 1);
                DrawStatusInline(color, ready ? "Attached & Ready" : "Attach in Home tab first");

                if (!ready) return;

                var cam = DayZUpdater.DayZCameraSnapshots.Current;
                var ents = DayZUpdater.EntitySnapshots.Current;

                // ESP drawing
                if (cfg.ShowPlayers)
                    foreach (var p in ents.Where(e => e.Category == EntityType.Player))
                        DrawBoxAround(p.Position, cfg.PlayerColor, cam);

                if (cfg.ShowZombies)
                    foreach (var z in ents.Where(e => e.Category == EntityType.Zombie))
                        DrawBoxAround(z.Position, cfg.ZombieColor, cam);

                if (cfg.ShowLoot)
                    foreach (var item in ents.Where(e =>
                        e.Category == EntityType.Weapon || e.Category == EntityType.Ammo || e.Category == EntityType.Food))
                        DrawBoxAround(item.Position, cfg.ItemColor, cam);

                // Options
                ImGui.Separator();
                bool showDebugOverlay = cfg.ShowDebugOverlay;
                if (ImGui.Checkbox("Show Debug Overlay", ref showDebugOverlay))
                    cfg.ShowDebugOverlay = showDebugOverlay;
                float debugDistance = cfg.DebugDistance;
                if (ImGui.SliderFloat("Debug Distance", ref debugDistance, 50f, 2000f))
                    cfg.DebugDistance = debugDistance;

                bool showRawDebug = cfg.ShowRawDebug;
                if (ImGui.Checkbox("Show Raw Debug Window", ref showRawDebug))
                    cfg.ShowRawDebug = showRawDebug;

                ImGui.Separator();
                if (ImGui.Button(_running ? "Restart Workers" : "Start Workers"))
                {
                    if (_running) { Stop(); Start(); }
                    else { Start(); }
                }
                ImGui.SameLine();
                if (ImGui.Button("Stop Workers")) Stop();
            });

            // ─────────────────────────────
            // Separate Debug Window
            // ─────────────────────────────
            if (Cfg.ShowRawDebug)
            {
                ImGui.Begin("DayZ Debug", ImGuiWindowFlags.AlwaysAutoResize);

                var snap = DayZSnapshots.Current;
                var cam = DayZUpdater.DayZCameraSnapshots.Current;
                var ents = DayZUpdater.EntitySnapshots.Current;

                if (ImGui.CollapsingHeader("World / Manager", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Text($"WorldPtr: 0x{DayZUpdater.WorldPtr:X}");
                    ImGui.Text($"NetMgrPtr: 0x{DayZUpdater.NetMgrPtr:X}");
                    ImGui.Text($"World: 0x{snap.World:X}");
                    ImGui.Text($"NetworkMgr: 0x{snap.NetworkManager:X}");
                }

                if (ImGui.CollapsingHeader("Camera", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (cam != null)
                    {
                        ImGui.Text($"ViewTranslation: {cam.InvertedViewTranslation}");
                        ImGui.Text($"Forward: {cam.InvertedViewForward}");
                        ImGui.Text($"Right: {cam.InvertedViewRight}");
                        ImGui.Text($"Up: {cam.InvertedViewUp}");
                    }
                    else
                        ImGui.TextDisabled("Camera not available");
                }

                if (ImGui.CollapsingHeader("Entity Tables", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Text($"Near: {snap.NearCount}");
                    ImGui.Text($"Far: {snap.FarCount}");
                    ImGui.Text($"Slow: {snap.SlowCount}");
                    ImGui.Text($"Items: {snap.ItemCount}");
                }

                if (ImGui.CollapsingHeader("Entities", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (ImGui.BeginTabBar("EntityTabs"))
                    {
                        DrawEntityCategoryTab("Players", ents.Where(e => e.Category == EntityType.Player));
                        DrawEntityCategoryTab("Zombies", ents.Where(e => e.Category == EntityType.Zombie));
                        DrawEntityCategoryTab("Loot", ents.Where(e =>
                            e.Category == EntityType.Weapon || e.Category == EntityType.Ammo || e.Category == EntityType.Food));
                        DrawEntityCategoryTab("Other", ents.Where(e =>
                            e.Category != EntityType.Player && e.Category != EntityType.Zombie &&
                            e.Category != EntityType.Weapon && e.Category != EntityType.Ammo && e.Category != EntityType.Food));
                        ImGui.EndTabBar();
                    }
                }

                ImGui.End();
            }
        }

        private static void DrawEntityCategoryTab(string label, IEnumerable<Entity> ents)
        {
            if (ImGui.BeginTabItem(label))
            {
                if (ImGui.BeginChild($"{label}_Child", new Vector2(0, 300), ImGuiChildFlags.None))
                {
                    foreach (var e in ents.Take(200))
                        ImGui.Text($"0x{e.Ptr:X} | {e.CleanName} ({e.ConfigName}) pos=({e.Position.X:F1},{e.Position.Y:F1},{e.Position.Z:F1})");
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
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

        private static void DrawDebugOverlay(Entity ent, DayZCamera cam, float maxDist)
        {
            if (cam == null) return;

            if (!WorldToScreenDayZ(cam, ent.Position,
                    new Vector2(ScreenService.Current.W, ScreenService.Current.H), out var screenPos))
                return;

            float dist = Vector3.Distance(cam.InvertedViewTranslation, ent.Position);
            if (dist > maxDist) return;

            var dl = ImGui.GetForegroundDrawList();
            float y = screenPos.Y;

            void DrawLine(string text, uint col)
            {
                dl.AddText(new Vector2(screenPos.X, y), col, text);
                y += ImGui.GetFontSize();
            }

            uint green = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 1, 0, 1));
            DrawLine($"TypeName: {ent.TypeName}", green);
            DrawLine($"ConfigName: {ent.ConfigName}", green);
            DrawLine($"CleanName: {ent.CleanName}", green);
            DrawLine($"ModelName: {ent.ModelName}", green);
            DrawLine($"Pos: {ent.Position.X:F1}, {ent.Position.Y:F1}, {ent.Position.Z:F1}", green);
        }

        private static void DrawBoxAround(Vector3 worldPos, Vector4 color, DayZUpdater.DayZCamera cam, float size = 20f)
        {
            if (!DayZUpdater.WorldToScreenDayZ(cam, worldPos,
                    new Vector2(ScreenService.Current.W, ScreenService.Current.H), out var screenPos))
                return;

            var dl = ImGui.GetForegroundDrawList();
            uint col = ImGui.ColorConvertFloat4ToU32(color);

            float half = size * 0.5f;
            var min = new Vector2(screenPos.X - half, screenPos.Y - half);
            var max = new Vector2(screenPos.X + half, screenPos.Y + half);

            dl.AddRect(min, max, col, 0f, ImDrawFlags.None, 2f);
        }
    }

    public sealed class DayZConfig
    {
        public bool EnableESP { get; set; } = true;
        public float MaxDrawDistance { get; set; } = 1000f;
        public Vector4 PlayerColor { get; set; } = new(0f, 1f, 0f, 1f);
        public Vector4 ZombieColor { get; set; } = new(1f, 0f, 0f, 1f);
        public Vector4 CarColor { get; set; } = new(0f, 0.6f, 1f, 1f);
        public bool ShowPlayers { get; set; } = true;
        public bool ShowZombies { get; set; } = true;
        public bool ShowLoot { get; set; } = true;
        public Vector4 ItemColor { get; set; } = new(1f, 1f, 0f, 1f);

        public bool ShowDebugOverlay { get; set; } = false;
        public float DebugDistance { get; set; } = 200f;

        public bool ShowRawDebug { get; set; } = false;
    }
}
