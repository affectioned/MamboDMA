using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Runtime.InteropServices;
using ImGuiNET;
using MamboDMA.Services;
using static MamboDMA.OverlayWindow;
using static MamboDMA.Misc;

namespace MamboDMA.Games.ABI
{
    public sealed class ABIGame : IGame
    {
        public string Name => "ArenaBreakoutInfinite";

        private bool _initialized;
        private bool _running;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤ UI config ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private static bool _drawBoxes = true;
        private static bool _drawNames = true;
        private static bool _drawDistance = true;
        private static bool _drawSkeletons = false;
        private static bool _showDebug = false;

        private static float _maxDistance = 800f;
        private static float _maxSkeletonDistance = 300f;
        private static bool _radarEnabled = false;
        // Death markers config
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
            MamboDMA.Games.ABI.WebRadarUI.StopIfRunning(); // ensure server is down
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
            ImGui.Text("Base Colors (fallbacks, names)");
            ImGui.ColorEdit4("Player", ref _colorPlayer);
            ImGui.ColorEdit4("Bot", ref _colorBot);

            ImGui.Text("ESP Colors (by visibility)");
            ImGui.ColorEdit4("Box Visible", ref _colorBoxVisible);
            ImGui.ColorEdit4("Box Invisible", ref _colorBoxInvisible);
            ImGui.ColorEdit4("Skel Visible", ref _colorSkelVisible);
            ImGui.ColorEdit4("Skel Invisible", ref _colorSkelInvisible);

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
            {
                if (_running) Stop(); else Start();
            }
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

                var sd = Skeleton.LastDebug;
                if (sd.Mesh != 0)
                {
                    ImGui.Separator();
                    ImGui.Text("©¤ Skeleton Debug (last actor) ©¤");
                    ImGui.Text($"Mesh: 0x{sd.Mesh:X}");
                    ImGui.Text($"Note: {sd.Note ?? "(ok)"}");
                }

                DrawPlayersDebugWindow();
            }

            ImGui.End();

            // ESP Overlay
            if (_running)
            {
                if (Players.ActorList.Count > 0)
                {
                    ABIESP.Render(_drawBoxes, _drawNames, _drawDistance, _drawSkeletons,
                                  _drawDeathMarkers, _deathMarkerMaxDist, _deathMarkerBaseSize,
                                  _maxDistance, _maxSkeletonDistance,
                                  _colorPlayer, _colorBot,
                                  _colorBoxVisible, _colorBoxInvisible,
                                  _colorSkelVisible, _colorSkelInvisible,
                                  _deadFill, _deadOutline);
                }
                else if (_showDebug)
                {
                    var dl = ImGui.GetForegroundDrawList();
                    var io = ImGui.GetIO();
                    const string msg = "No actors found (check offsets or filter)";
                    var size = ImGui.CalcTextSize(msg);
                    dl.AddText(new Vector2(io.DisplaySize.X / 2 - size.X / 2, 60),
                        ImGui.GetColorU32(new Vector4(1, 0.8f, 0, 1)), msg);
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
            {
                for (int i = 0; i < fr.Positions.Count; i++)
                    posMap[fr.Positions[i].Pawn] = fr.Positions[i];
            }

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

    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // ESP (unchanged logic, now reads from Players.*)
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
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

            var cam    = fr.Cam;
            var local  = fr.Local;
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
                        DrawDiamond(list, screen, distM, deathMarkerBaseSize, ImGui.GetColorU32(deadFill), ImGui.GetColorU32(deadOutline));
                        if (drawDistance) list.AddText(new Vector2(screen.X - 14, screen.Y + (deathMarkerBaseSize + 4)), 0xFFFFFFFF, $"{distM:F1} m");
                    }
                    continue;
                }

                bool isVis = ap.IsVisible;
                uint clrName = ImGui.GetColorU32(actors[i].IsBot ? colorBot : colorPlayer);
                uint clrBox  = ImGui.GetColorU32(isVis ? colorBoxVisible : colorBoxInvisible);
                uint clrSkel = ImGui.GetColorU32(isVis ? colorSkelVisible : colorSkelInvisible);

                Vector2 min, max;

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
                        min = new Vector2(screen.X - w * 0.5f, cy - h * 0.5f);
                        max = new Vector2(screen.X + w * 0.5f, cy + h * 0.5f);
                    }
                    else
                    {
                        float bh = Math.Clamp(150f / MathF.Max(distM, 3f), 60f, 250f);
                        float bw = bh * 0.35f;
                        min = new(screen.X - bw / 2, screen.Y - bh / 2);
                        max = new(screen.X + bw / 2, screen.Y + bh / 2);
                    }

                    if (drawBoxes) DrawBox(list, min, max, clrBox, 1.5f);
                    if (drawNames) list.AddText(new Vector2((min.X + max.X) * 0.5f - 18, min.Y - 18), clrName, actors[i].IsBot ? "BOT" : "PMC");
                    if (drawDistance) list.AddText(new Vector2((min.X + max.X) * 0.5f - 12, max.Y + 4), 0xFFFFFFFF, $"{distM:F1} m");

                    if (ap.HealthMax > 1f)
                        DrawHealthBar(list, new Vector2(min.X, min.Y - 8f), max.X - min.X, ap.Health, ap.HealthMax);

                    if (drawSkeletons && distM <= maxSkelDistMeters)
                        Skeleton.Draw(list, bones, cam, scrW, scrH, clrSkel);
                }
                else
                {
                    float bh = Math.Clamp(150f / MathF.Max(distM, 3f), 60f, 250f);
                    float bw = bh * 0.35f;
                    var min2 = new Vector2(screen.X - bw / 2, screen.Y - bh / 2);
                    var max2 = new Vector2(screen.X + bw / 2, screen.Y + bh / 2);

                    if (drawBoxes) DrawBox(list, min2, max2, clrBox, 1.5f);
                    if (drawNames) list.AddText(new Vector2((min2.X + max2.X) * 0.5f - 18, min2.Y - 18), clrName, actors[i].IsBot ? "BOT" : "PMC");
                    if (drawDistance) list.AddText(new Vector2((min2.X + max2.X) * 0.5f - 12, max2.Y + 4), 0xFFFFFFFF, $"{distM:F1} m");

                    if (ap.HealthMax > 1f)
                        DrawHealthBar(list, new Vector2(min2.X, min2.Y - 8f), max2.X - min2.X, ap.Health, ap.HealthMax);
                }
            }
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
        public static bool IsBogusPos(in Vector3 p)
        {
            // Treat (0,0,-90) as the sentinel. Use small eps so tiny float jitter doesn't leak through.
            const float ex = 0.5f;    // X/Y tolerance
            const float ez = 1.0f;    // Z tolerance
            return MathF.Abs(p.X) <= ex && MathF.Abs(p.Y) <= ex && MathF.Abs(p.Z + 90f) <= ez;
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

    // Timer resolution helper (unchanged)
    internal static class TimerResolution
    {
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uMilliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uMilliseconds);
        private static int _ref;
        public static void Enable1ms() { if (Interlocked.Increment(ref _ref) == 1) timeBeginPeriod(1); }
        public static void Disable1ms(){ if (Interlocked.Decrement(ref _ref) == 0) timeEndPeriod(1); }
    }
}
