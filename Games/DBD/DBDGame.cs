using ImGuiNET;
using MamboDMA.Games.CS2;
using MamboDMA.Services;
using System.Numerics;

namespace MamboDMA.Games.DBD
{
    public sealed class DBDGame : IGame
    {
        public string Name => "DeadByDaylight";

        private bool _initialized;
        private bool _running;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ UI config ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static bool _showDebug = false;

        private string _dbdExe = "DeadByDaylight-Win64-Shipping.exe";
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

            if (ImGui.BeginTabBar("DBD_Tabs", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton))
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
                        ImGui.InputText("Process Name", ref _dbdExe, 128);
                        if (!attached)
                        {
                            if (ImGui.Button($"Attach ({_dbdExe})")) Attach();
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

                if (_showDebug)
                {
                    ImGui.Separator();
                    ImGui.Text("©¤ Debug Info ©¤");
                    ImGui.Text("©¤ IDA Signature Resolve ©¤");
                    ImGui.Text($"GWorld: 0x{DBDOffsets.GWorld:X}");
                    ImGui.Text($"GNames: 0x{DBDOffsets.GNames:X}");
                    ImGui.Text($"GObjects: 0x{DBDOffsets.GObjects:X}");
                    ImGui.Text("©¤ Dynamically Resolved Offsets ©¤");
                    ImGui.Text($"UWorld: 0x{DBDPlayers.UWorld:X}");

                    ImGui.SameLine();
                    if (ImGui.Button("Dispose VMM")) Dispose();
                }

                if (_running)
                {

                }

                ImGui.EndTabBar();
            }
        }

        public void Initialize()
        {
            if (_initialized) return;
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);
            _initialized = true;
        }

        public void Start()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            DBDPlayers.StartCache();
            Logger.Info("[DBD] players cache threads started");
        }

        private void Attach() => VmmService.Attach(_dbdExe);

        public void Stop()
        {
            if (!_running) return;
            _running = false;
        }

        // TODO: same here
        private void Dispose()
        {
            Stop();
            DmaMemory.Dispose();
        }

        public void Tick() {}

        // TODO: REFACTOR TO IGAME maybe or a class for games?
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
