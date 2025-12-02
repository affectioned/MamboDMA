using ImGuiNET;
using MamboDMA.Games.ABI;
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
        private static bool _drawLines = true;
        private static bool _drawBoxes = true;
        private static bool _drawNames = true;
        private static bool _drawDistance = true;
        private static bool _drawSkeletons = false;
        private static bool _showDebug = false;
        private static bool _showEntityDebug = false;
        private static bool _showLocalPlayerDebug = false;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ TODO: put to Config Class ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Label colors
        public Vector4 ColorPlayer = new(1f, 0.25f, 0.25f, 1f);
        public Vector4 ColorBot = new(0f, 0.6f, 1f, 1f);

        // ESP colors
        public Vector4 ColorBoxVisible = new(0.20f, 1.00f, 0.20f, 1f);
        public Vector4 ColorBoxInvisible = new(1.00f, 0.50f, 0.00f, 1f);
        public Vector4 ColorSkelVisible = new(1.00f, 1.00f, 1.00f, 1f);
        public Vector4 ColorSkelInvisible = new(0.70f, 0.70f, 0.70f, 1f);
        public Vector4 ColorLineVisible = new(0.20f, 1.00f, 0.20f, 1f);
        public Vector4 ColorLineInvisible = new(1.00f, 0.50f, 0.00f, 1f);
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ TODO: put to Config Class ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤

        private string _cs2Exe = "cs2.exe";
        public void Draw(ImGuiWindowFlags winFlags)
        {
            bool vmmReady = DmaMemory.IsVmmReady;
            bool attached = DmaMemory.IsAttached;

            var statusCol = (attached && _running) ? new Vector4(0, 0.8f, 0, 1) :
                            attached ? new Vector4(0.85f, 0.75f, 0.15f, 1) :
                                       new Vector4(1, 0.3f, 0.2f, 1);
            DrawStatusInline(statusCol,
                attached ? (_running ? "Attached ¡¤ Threads running" : "Attached ¡¤ Threads stopped")
                         : "Not attached");

            ImGui.Separator();

            if (ImGui.BeginTabBar("CS2_Tabs", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton))
            {
                // MAIN
                if (ImGui.BeginTabItem("Main"))
                {
                    ImGui.TextDisabled("VMM & Attach");
                    if (!vmmReady)
                    {
                        if (ImGui.Button("Init VMM")) VmmService.InitOnly();
                        ImGui.SameLine(); ImGui.TextDisabled("¡û initialize before attaching");
                    }
                    else
                    {
                        ImGui.InputText("Process Name", ref _cs2Exe, 128);
                        if (!attached)
                        {
                            if (ImGui.Button($"Attach ({_cs2Exe})")) Attach();
                            ImGui.SameLine(); ImGui.TextDisabled("¡û attaches without process picker");
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Dispose VMM")) Dispose();

                    ImGui.Separator();

                    if (!attached) ImGui.BeginDisabled();
                    if (ImGui.Button(_running ? "Stop Threads" : "Start Threads"))
                    { if (_running) Stop(); else Start(); }
                    if (!attached) ImGui.EndDisabled();

                    ImGui.Separator();

                    ImGui.EndTabItem();
                }

                // ESP
                if (ImGui.BeginTabItem("ESP"))
                {
                    if (ImGui.CollapsingHeader("Basics", ImGuiTreeNodeFlags.DefaultOpen))
                    {

                        ImGui.Checkbox("Draw Boxes", ref _drawBoxes);
                        ImGui.Checkbox("Draw Names", ref _drawNames);
                        ImGui.Checkbox("Draw Distance", ref _drawDistance);
                        ImGui.Checkbox("Draw Skeletons", ref _drawSkeletons);
                        ImGui.Checkbox("Show Debug Info", ref _showDebug);

                        // NOT IMPLEMENTED YET
                        //ImGui.SliderFloat("Max Draw Distance (m)", ref cfg.MaxDistance, 50f, 3000f, "%.0f");
                        //ImGui.SliderFloat("Skeleton Draw Distance (m)", ref cfg.MaxSkeletonDistance, 25f, 2000f, "%.0f");
                    }

                    ImGui.EndTabItem();
                }

                // NOT IMPLEMENTED YET TODO
                // WEBRADAR
                //if (ImGui.BeginTabItem("WebRadar"))
                //{
                //    WebRadarUI.DrawPanel();
                //    ImGui.EndTabItem();
                //}

                // COLORS
                if (ImGui.BeginTabItem("Colors"))
                {
                    ImGui.Text("ESP Colors");
                    ImGui.ColorEdit4("Box Visible", ref ColorBoxVisible);
                    ImGui.ColorEdit4("Box Invisible", ref ColorBoxInvisible);
                    ImGui.ColorEdit4("Skel Visible", ref ColorSkelVisible);
                    ImGui.ColorEdit4("Skel Invisible", ref ColorSkelInvisible);

                    ImGui.Separator();
                    ImGui.Text("Base Labels");
                    ImGui.ColorEdit4("Player", ref ColorPlayer);
                    ImGui.ColorEdit4("Bot", ref ColorBot);

                    ImGui.EndTabItem();
                }

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
                    ImGui.Checkbox("Show LocalPlayer Debug Info", ref _showLocalPlayerDebug);

                    if (_showEntityDebug)
                    {
                        DrawEntitiesDebugWindow();
                    }

                    if (_showLocalPlayerDebug) 
                    {
                        DrawLocalPlayerDebugWindow();
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Dispose VMM")) Dispose();
                }

                if (_running)
                {
                    CS2ESP.Render(
                        _drawBoxes,
                        _drawNames,
                        _drawSkeletons,
                        ColorPlayer,
                        ColorBot,
                        ColorBoxVisible,
                        ColorBoxInvisible,
                        ColorSkelVisible,
                        ColorSkelInvisible
                    );
                }

                ImGui.EndTabBar();
            }
        }

        // ---------- existing debug table ----------
        private static void DrawEntitiesDebugWindow()
        {
            var list = CS2Entities.GetCachedEntitiesSnapshot().ToArray();

            ImGui.Begin("CS2 Entity Debug", ImGuiWindowFlags.None);

            if (ImGui.BeginTable("cs2_dbg_tbl", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(1150, 400)))
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
                    var p = e.Origin;
                    string posStr = $"{p.X:F0}, {p.Y:F0}, {p.Z:F0}";

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(i.ToString());
                    ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(e.LifeState.ToString());
                    ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(e.Health.ToString());
                    ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(e.Team.ToString());
                    ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted(posStr);
                    ImGui.TableSetColumnIndex(5); ImGui.TextUnformatted(e.Name);
                }

                ImGui.EndTable();
            }

            ImGui.End();
        }

        private static void DrawLocalPlayerDebugWindow()
        {
            ImGui.Begin("CS2 Entity Debug", ImGuiWindowFlags.None);

            if (ImGui.BeginTable("cs2_dbg_tbl", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(1150, 400)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("LifeState");
                ImGui.TableSetupColumn("Health");
                ImGui.TableSetupColumn("Team");
                ImGui.TableSetupColumn("Origin");
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("ViewMatrix");
                ImGui.TableHeadersRow();

                var lp = CS2Entities.LocalPlayer;
                var p = lp.Origin;
                string posStr = $"{p.X:F0}, {p.Y:F0}, {p.Z:F0}";

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(lp.LifeState.ToString());
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(lp.Health.ToString());
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(lp.Team.ToString());
                ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(posStr);
                ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted(lp.Name);
                ImGui.TableSetColumnIndex(5); ImGui.TextUnformatted(CS2Entities.localViewMatrix.ToString());

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
