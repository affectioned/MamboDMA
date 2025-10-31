using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using ImGuiNET;
using MamboDMA.Services;
using static MamboDMA.OverlayWindow;
using static MamboDMA.Misc;

namespace MamboDMA.Games.ABI
{
    public sealed class ABIGame : IGame
    {
        public string Name => "ArenaBreakoutInfinite";
        private bool _initialized, _running;

        private static bool _drawBoxes = true;
        private static bool _drawNames = true;
        private static bool _drawDistance = true;
        private static bool _drawSkeletons = false;
        private static bool _showDebug = false;

        private static float _maxDistance = 800f;
        private static float _maxSkeletonDistance = 300f;

        private static bool  _drawDeathMarkers = true;
        private static float _deathMarkerMaxDist = 1200f;
        private static float _deathMarkerBaseSize = 10f;

        private static Vector4 _colorPlayer = new(1f, 0.25f, 0.25f, 1f);
        private static Vector4 _colorBot    = new(0f, 0.6f, 1f, 1f);

        private static Vector4 _colorBoxVisible    = new(0.20f, 1.00f, 0.20f, 1f);
        private static Vector4 _colorBoxInvisible  = new(1.00f, 0.50f, 0.00f, 1f);
        private static Vector4 _colorSkelVisible   = new(1.00f, 1.00f, 1.00f, 1f);
        private static Vector4 _colorSkelInvisible = new(0.70f, 0.70f, 0.70f, 1f);

        private static Vector4 _deadFill    = new(0, 0, 0, 1f);
        private static Vector4 _deadOutline = new(1f, 0.84f, 0f, 1f);

        private const string _abiExe = "UAGame.exe";

        public void Initialize()
        {
            if (_initialized) return;
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);
            _initialized = true;
        }

        public void Attach() => VmmService.Attach(_abiExe);

        public void Dispose()
        {
            Stop();
            DmaMemory.Dispose();
        }

        public void Start()
        {
            if (_running || !DmaMemory.IsAttached) return;
            _running = true;

            TimerResolution.Enable1ms();
            Players.StartCache();
            Logger.Info("[ABI] players cache threads started");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            Players.Stop();
            TimerResolution.Disable1ms();
            WebRadarUI.StopIfRunning();
            Logger.Info("[ABI] players cache threads stopped");
        }

        public void Tick() { }

        public void Draw(ImGuiWindowFlags flags)
        {
            ImGui.Begin("Arena Breakout Infinite", flags | ImGuiWindowFlags.AlwaysAutoResize);

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
                if (ImGui.Button($"Attach ({_abiExe})")) Attach();
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
            ImGui.SliderFloat("Max Draw Distance (m)", ref _maxDistance, 50f, 3000f);
            ImGui.SliderFloat("Skeleton Draw Distance (m)", ref _maxSkeletonDistance, 25f, 2000f);

            ImGui.Separator();
            ImGui.Text("ESP Colors");
            ImGui.ColorEdit4("Box Visible", ref _colorBoxVisible);
            ImGui.ColorEdit4("Box Invisible", ref _colorBoxInvisible);
            ImGui.ColorEdit4("Skel Visible", ref _colorSkelVisible);
            ImGui.ColorEdit4("Skel Invisible", ref _colorSkelInvisible);

            ImGui.Text("Base Labels");
            ImGui.ColorEdit4("Player", ref _colorPlayer);
            ImGui.ColorEdit4("Bot", ref _colorBot);

            ImGui.Text("Dead Marker");
            ImGui.Checkbox("Enable Death Markers", ref _drawDeathMarkers);
            ImGui.SliderFloat("Max Marker Distance (m)", ref _deathMarkerMaxDist, 50f, 5000f);
            ImGui.SliderFloat("Marker Base Size (px)", ref _deathMarkerBaseSize, 4f, 24f);
            ImGui.ColorEdit4("Dead Fill", ref _deadFill);
            ImGui.ColorEdit4("Dead Outline", ref _deadOutline);

            ImGui.Separator();
            WebRadarUI.DrawPanel();

            ImGui.Separator();
            if (!attached) ImGui.BeginDisabled();
            if (ImGui.Button(_running ? "Stop Threads" : "Start Threads"))
            { if (_running) Stop(); else Start(); }
            if (!attached) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Dispose VMM")) Dispose();

            if (!attached) { ImGui.End(); return; }

            if (_showDebug)
            {
                ImGui.Separator();
                ImGui.Text("©¤ Debug Info ©¤");
                ImGui.Text($"UWorld: 0x{Players.UWorld:X}");
                ImGui.Text($"UGameInstance: 0x{Players.UGameInstance:X}");
                ImGui.Text($"GameState: 0x{Players.GameState:X}");
                ImGui.Text($"PersistentLevel: 0x{Players.PersistentLevel:X}");
                ImGui.Text($"ActorCount: {Players.ActorCount}");
                ImGui.Text($"ActorList.Count: {Players.ActorList.Count}");
                ImGui.Text($"LocalPawn: 0x{Players.LocalPawn:X}");
                ImGui.Text($"LocalRoot: 0x{Players.LocalRoot:X}");
                ImGui.Text($"CameraMgr: 0x{Players.LocalCameraMgr:X}");
                ImGui.Text($"CameraFov: {Players.Camera.Fov:F1}");
                ImGui.Text($"LocalPos: {Players.LocalPosition}");
                ImGui.Text($"CtrlYaw: {Players.CtrlYaw:F1}");

                DrawPlayersDebugWindow();
            }

            ImGui.End();

            if (_running)
            {
                if (Players.ActorList.Count > 0)
                {
                    ABIESP.Render(
                        _drawBoxes, _drawNames, _drawDistance, _drawSkeletons,
                        _drawDeathMarkers, _deathMarkerMaxDist, _deathMarkerBaseSize,
                        _maxDistance, _maxSkeletonDistance,
                        _colorPlayer, _colorBot,
                        _colorBoxVisible, _colorBoxInvisible,
                        _colorSkelVisible, _colorSkelInvisible,
                        _deadFill, _deadOutline);
                }
            }
        }

        private static void DrawPlayersDebugWindow()
        {
            if (!Players.TryGetFrame(out var fr)) return;

            ImGui.Begin("ABI Player Debug", ImGuiWindowFlags.None);

            var actors = new List<Players.ABIPlayer>();
            lock (Players.Sync)
            {
                if (Players.ActorList.Count > 0)
                    actors = new List<Players.ABIPlayer>(Players.ActorList);
            }

            var posMap = new Dictionary<ulong, Players.ActorPos>(fr.Positions?.Count ?? 0);
            if (fr.Positions != null)
                for (int i = 0; i < fr.Positions.Count; i++)
                    posMap[fr.Positions[i].Pawn] = fr.Positions[i];

            if (ImGui.BeginTable("abi_dbg_tbl", 12, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(1160, 400)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 24);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 44);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthFixed, 88);
                ImGui.TableSetupColumn("DeathInfo.bIsDead", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Position (X,Y,Z)", ImGuiTableColumnFlags.WidthFixed, 240);
                ImGui.TableSetupColumn("Skel", ImGuiTableColumnFlags.WidthFixed, 44);
                ImGui.TableSetupColumn("Visible", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Dist(m)", ImGuiTableColumnFlags.WidthFixed, 62);
                ImGui.TableSetupColumn("Pawn", ImGuiTableColumnFlags.WidthFixed, 110);
                ImGui.TableSetupColumn("MeshPtr", ImGuiTableColumnFlags.WidthFixed, 110);
                ImGui.TableSetupColumn("DeathComp", ImGuiTableColumnFlags.WidthFixed, 110);
                ImGui.TableHeadersRow();

                for (int i = 0; i < actors.Count; i++)
                {
                    var a = actors[i];
                    posMap.TryGetValue(a.Pawn, out var ap);
                    if (ABIESP.IsBogusPos(ap.Position)) continue;
                    string type = a.IsBot ? "BOT" : "PMC";
                    string hp = (ap.HealthMax > 1f) ? $"{ap.Health:F0}/{ap.HealthMax:F0}" : "-";
                    bool hasSkel = ap.HasFreshSkeleton;
                    bool visible = ap.IsVisible;
                    float distM = fr.Local != default ? Vector3.Distance(fr.Local, ap.Position) / 100f : 0f;

                    var p = ap.Position;
                    string posStr = $"{p.X:F0}, {p.Y:F0}, {p.Z:F0}";

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);  ImGui.TextUnformatted(i.ToString());
                    ImGui.TableSetColumnIndex(1);  ImGui.TextUnformatted(type);
                    ImGui.TableSetColumnIndex(2);  ImGui.TextUnformatted(a.Name ?? "");
                    ImGui.TableSetColumnIndex(3);  ImGui.TextUnformatted(hp);
                    ImGui.TableSetColumnIndex(4);  ImGui.TextUnformatted(ap.DeadByDeathComp ? "true" : "false");
                    ImGui.TableSetColumnIndex(5);  ImGui.TextUnformatted(posStr);
                    ImGui.TableSetColumnIndex(6);  ImGui.TextUnformatted(hasSkel ? "fresh" : "no");
                    ImGui.TableSetColumnIndex(7);  ImGui.TextUnformatted(visible ? "yes" : "no");
                    ImGui.TableSetColumnIndex(8);  ImGui.TextUnformatted(distM > 0 ? distM.ToString("F1") : "-");
                    ImGui.TableSetColumnIndex(9);  ImGui.TextUnformatted($"0x{a.Pawn:X}");
                    ImGui.TableSetColumnIndex(10); ImGui.TextUnformatted(a.Mesh != 0 ? $"0x{a.Mesh:X}" : "-");
                    ImGui.TableSetColumnIndex(11); ImGui.TextUnformatted(a.DeathComp != 0 ? $"0x{a.DeathComp:X}" : "-");
                }

                ImGui.EndTable();
            }

            ImGui.End();
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
    }
}
