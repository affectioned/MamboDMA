using ImGuiNET;
using MamboDMA.Services;
using System.Numerics;

namespace MamboDMA.Games.CS2
{
    public sealed class CS2Game : IGame
    {
        public string Name => "CounterStrike2";

        private bool _initialized;
        private bool _running;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ UI config ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static bool _drawBoxes = true;
        private static bool _drawNames = true;
        private static bool _drawDistance = true;
        private static bool _drawSkeletons = false;
        private static bool _showDebug = false;
        private static bool _showEntityDebug = false;

        private static Vector4 _colorFriendly = new(1f, 0.25f, 0.25f, 1f);
        private static Vector4 _colorEnemy = new(0f, 0.6f, 1f, 1f);

        private const string _cs2Exe = "cs2.exe";
        public void Draw(ImGuiWindowFlags winFlags)
        {
            if (!ImGui.Begin("Counter Strike 2", winFlags | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.End();
                return;
            }

            bool vmmReady = DmaMemory.IsVmmReady;
            bool attached = DmaMemory.IsAttached;

            ImGui.TextDisabled("Quick Setup");
            if (!vmmReady)
            {
                if (ImGui.Button("Init VMM")) VmmService.InitOnly();
                ImGui.SameLine(); ImGui.TextDisabled("¡û initialize before attaching");
            }
            else if (!attached)
            {
                if (ImGui.Button($"Attach ({_cs2Exe})")) Attach();
                ImGui.SameLine(); ImGui.TextDisabled("¡û attaches without process picker");
            }

            var statusCol = (attached && _running) ? new Vector4(0, 0.8f, 0, 1) :
                            attached ? new Vector4(0.85f, 0.75f, 0.15f, 1) :
                                       new Vector4(1, 0.3f, 0.2f, 1);
            DrawStatusInline(statusCol,
                attached ? (_running ? "Attached ¡¤ Threads running" : "Attached ¡¤ Threads stopped")
                         : "Not attached");

            ImGui.Separator();
            ImGui.Checkbox("Draw Boxes", ref _drawBoxes);
            ImGui.Checkbox("Draw Names", ref _drawNames);
            ImGui.Checkbox("Draw Distance", ref _drawDistance);
            ImGui.Checkbox("Draw Skeletons", ref _drawSkeletons);
            ImGui.Checkbox("Show Debug Info", ref _showDebug);

            ImGui.Separator();
            ImGui.Text("Base Colors (fallbacks, names)");
            ImGui.ColorEdit4("Friendly", ref _colorFriendly);
            ImGui.ColorEdit4("Enemy", ref _colorEnemy);

            ImGui.Separator();

            if (!attached) ImGui.BeginDisabled();
            if (ImGui.Button(_running ? "Stop Threads" : "Start Threads"))
            {
                if (_running) Stop(); else Start();
            }
            if (!attached) ImGui.EndDisabled();

            if (_showDebug)
            {
                ImGui.Separator();
                ImGui.Text("©¤ Debug Info ©¤");
                ImGui.Text($"clientBase: 0x{CS2Entities.clientBase:X}");
                ImGui.Text($"entityListPtr: 0x{CS2Entities.entityListPtr:X}");
                ImGui.Text("©¤ 64 controllers ©¤");
                ImGui.Text($"listEntry: 0x{CS2Entities.listEntry:X}");
                ImGui.Text($"controllerBase: 0x{CS2Entities.controllerBase:X}");
                ImGui.Text($"playerPawn: 0x{CS2Entities.playerPawn:X}");
                ImGui.Text($"listEntry2: 0x{CS2Entities.listEntry2:X}");
                ImGui.Text($"addressBase: 0x{CS2Entities.addressBase:X}");
                ImGui.Checkbox("Show Entity Debug Info", ref _showEntityDebug);

                if (_showEntityDebug)
                {
                    DrawEntitiesDebugWindow();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Dispose VMM")) Dispose();

            ImGui.End();
        }

        private static void DrawEntitiesDebugWindow()
        {
            ImGui.Begin("CS2 Entity Debug", ImGuiWindowFlags.None);

            var list = CS2Entities.GetCachedEntitiesSnapshot().ToArray();

            if (ImGui.BeginTable("cs2_dbg_tbl", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(1150, 400)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 24);
                ImGui.TableSetupColumn("LifeState");
                ImGui.TableSetupColumn("Health");
                ImGui.TableSetupColumn("Team");
                ImGui.TableSetupColumn("Origin");
                ImGui.TableSetupColumn("Name");
                ImGui.TableHeadersRow();

                for (int i = 0; i < list.Length; i++)
                {
                    var e = list[i];
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(i.ToString());
                    ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(e.LifeState.ToString());
                    ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(e.Health.ToString());
                    ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(e.Team.ToString());
                    ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted(e.Origin.ToString());
                    ImGui.TableSetColumnIndex(5); ImGui.TextUnformatted(e.Name);
                }

                ImGui.EndTable();
            }

            ImGui.End();
        }

        public void Initialize()
        {
            if (_initialized) return;
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);
            _initialized = true;
        }

        private void Attach() => VmmService.Attach(_cs2Exe);

        private void Dispose()
        {
            Stop();
            DmaMemory.Dispose();
        }

        public void Start()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            CS2Entities.StartCache();
            Logger.Info("[CS2] entity cache threads started");

            //Players.StartCache();
            //Logger.Info("[CS2] entity cache threads started");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
        }

        public void Tick() {}

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
    }
}
