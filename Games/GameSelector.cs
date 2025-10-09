// MamboDMA/Games/GameSelector.cs
using System.Linq;
using System.Numerics;
using ImGuiNET;
using MamboDMA.Gui;
using MamboDMA.Services;
using Raylib_cs;
using static MamboDMA.Misc;
using static MamboDMA.OverlayWindow;
using static MamboDMA.StyleEditorUI;

namespace MamboDMA.Games
{
    /// <summary>ImGui dropdown to switch among all GameRegistry-registered games.</summary>
    public static class GameSelector
    {
        private static string _exe = "example.exe";
        private static int _selectedMonitor = 0;
        private static bool _applyBorderless = false;
        private static bool _applyFullscreen = false;
        private static bool _undecoratedApplied = false;
        private static string _appName = "MamboDMA";
        static bool _uiUseVsync = false;
        static int _uiFpsCap = 144;
        private static int _procSelectedIndex = -1;
        private static int _selectedInputIndex = -1;
        private static List<MamboDMA.Input.Device.SerialDeviceInfo> _inputDevices = new();
        private static bool _inputInitialized = false;
        public static int SelectedMonitor => _selectedMonitor;
        public static void Draw()
        {
            // Let the active game do light per-frame pumping
            GameHost.Tick();
            Assets.Load(); // ensure logo loaded
            Theme.DrawThemePanel();        
            // Home window: game selector + active game panel
            ImGui.Begin("Home");
            DrawCombo("Game");
            ImGui.Separator();
            GameHost.Draw(ImGuiWindowFlags.None);   // draw the selected game's UI here
            ImGui.Separator();
            ImGui.End();

            // Service · Control window (manual VMM actions only when you click)
            ImGui.Begin("Service · Control", ImGuiWindowFlags.NoCollapse);

            var s = Snapshots.Current;

            // ─────────────────────────────────────────────────────────────────────────────
            // Top status (folded)
            bool fStatus = BeginFold("svc.status", "Status", defaultOpen: true);
            if (fStatus)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), s.Status);
                ImGui.Separator();
                EndFold(fStatus);
            }

            // ─────────────────────────────────────────────────────────────────────────────
            // VMM controls (folded)
            bool fVmm = BeginFold("svc.vmm", "VMM Controls", defaultOpen: true);
            if (fVmm)
            {
                if (ImGui.Button("Init VMM (no attach)")) VmmService.InitOnly();
                ImGui.SameLine();
                if (ImGui.Button("Dispose VMM")) VmmService.DisposeVmm();

                ImGui.Separator();
                ImGui.TextDisabled($"VMM Ready: {s.VmmReady} | PID: {s.Pid} | Base: 0x{s.MainBase:X}");
                EndFold(fVmm);
            }

            // ─────────────────────────────────────────────────────────────────────────────
            // Attach & Processes (folded)
            bool fProc = BeginFold("svc.proc", "Attach & Processes", defaultOpen: false);
            if (fProc)
            {
                // Manual refresh
                if (ImGui.Button("Refresh Processes"))
                    VmmService.RefreshProcesses();

                ImGui.Separator();
                ImGui.Text("Processes:");

                if (ImGui.BeginChild("proc_child", new Vector2(0, 150), ImGuiChildFlags.None))
                {
                    for (int i = 0; i < s.Processes.Count(); i++)
                    {
                        var p = s.Processes[i];
                        bool sel = (i == _procSelectedIndex);
                        if (ImGui.Selectable($"{p.Pid,6}  {p.Name}{(p.IsWow64 ? " (Wow64)" : "")}", sel))
                            _procSelectedIndex = i;
                    }
                    ImGui.EndChild();
                }

                if (_procSelectedIndex >= 0 && _procSelectedIndex < s.Processes.Count())
                {
                    var chosen = s.Processes[_procSelectedIndex];
                    ImGui.TextColored(new Vector4(0.6f, 1f, 0.6f, 1f),
                        $"Selected: {chosen.Name} (PID {chosen.Pid})");

                    if (ImGui.Button("Attach Selected"))
                        VmmService.Attach(chosen.Name);
                }
                else
                {
                    ImGui.TextDisabled("No process selected.");
                }
                EndFold(fProc);
            }

            // ─────────────────────────────────────────────────────────────────────────────
            // Modules (folded)
            bool fMods = BeginFold("svc.mods", "Modules", defaultOpen: false);
            if (fMods)
            {
                if (ImGui.Button("Refresh Modules")) VmmService.RefreshModules();
                ImGui.Text("Modules:");
                if (ImGui.BeginChild("mods_child", new Vector2(0, 150), ImGuiChildFlags.None))
                {
                    foreach (var m in s.Modules)
                        ImGui.TextUnformatted($"{m.Name,-28} Base=0x{m.Base:X} Size=0x{m.Size:X}");
                    ImGui.EndChild();
                }
                EndFold(fMods);
            }

            // ─────────────────────────────────────────────────────────────────────────────
            // Display & Frame pacing (folded)
            bool fDisp = BeginFold("svc.display", "Display & Frame Pacing", defaultOpen: true);
            if (fDisp)
            {
                DrawMonitorSettings();   // uses your existing function
                EndFold(fDisp);
            }
            // ─────────────────────────────────────────────────────────────────────────────
            // Input Manager & Makcu (folded)
            DrawInputManager();
            // ─────────────────────────────────────────────────────────────────────────────
            // Top Info Bar
            DrawTopInfoBar();

            ImGui.End();
        }


        public static void DrawCombo(string label = "Game")
        {
            var names = GameRegistry.Names?.ToArray() ?? System.Array.Empty<string>();
            if (names.Length == 0)
            {
                ImGui.TextDisabled("No games registered");
                return;
            }

            // If nothing active yet, pick the first (does not Start() workers)
            if (GameRegistry.Active is null)
                GameRegistry.Select(names[0]);

            var activeName = GameRegistry.Active?.Name ?? names[0];
            int cur = System.Array.IndexOf(names, activeName);
            if (cur < 0) cur = 0;

            ImGui.SetNextItemWidth(250);
            if (ImGui.BeginCombo(label, names[cur]))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    bool selected = (i == cur);
                    if (ImGui.Selectable(names[i], selected) && i != cur)
                        GameRegistry.Select(names[i]);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        public static void DrawMonitorSettings()
        {
            ImGui.Checkbox("Use VSync (syncs to monitor refresh)", ref _uiUseVsync);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.InputInt("FPS Cap", ref _uiFpsCap);

            if (ImGui.Button("Apply Frame Pacing"))
            {
                // push values into the window instance (store a reference, or expose a setter)
                OverlayWindowApi.SetFramePacing(_uiUseVsync, _uiFpsCap);
            }
            int monCount = Raylib.GetMonitorCount();
            if (_selectedMonitor >= monCount) _selectedMonitor = Math.Max(0, monCount - 1);

            var labels = new List<string>(monCount);
            for (int i = 0; i < monCount; i++)
                labels.Add($"Monitor {i}  ({Raylib.GetMonitorWidth(i)}×{Raylib.GetMonitorHeight(i)})  @{Raylib.GetMonitorRefreshRate(i)}Hz");

            using (UiLayout.PushFieldWidth(260, 420))
            {
                string preview = monCount > 0 ? labels[_selectedMonitor] : "(no monitors)";
                if (ImGui.BeginCombo("Monitor", preview))
                {
                    for (int i = 0; i < monCount; i++)
                    {
                        bool sel = (i == _selectedMonitor);
                        if (ImGui.Selectable(labels[i], sel)) _selectedMonitor = i;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            ImGui.Spacing();
            int b = UiLayout.ButtonRowAuto("Apply", "Center Window", "Restore Decorations");
            if (b == 0) // Apply
            {
                Misc.ApplyMonitorSelection(_selectedMonitor, _applyBorderless, _applyFullscreen);
            
                // update global screen settings
                ScreenService.UpdateFromMonitor(_selectedMonitor);
            }
            else if (b == 1)
            {
                Misc.CenterOnMonitor(_selectedMonitor);
            }
            else if (b == 2)
            {
                Misc.RestoreDecorations();
            }

            ImGui.Separator();
            ImGui.TextDisabled("Tip: Borderless + sizing to monitor gives a clean fullscreen feel without Alt-Tab quirks.");
        }
        private static Texture2D _logoTex;
        private static void EnsureLogo()
        {
            if (_logoTex.Id == 0)
                _logoTex = SvgLoader.LoadSvg("Assets/Img/Logo.svg", 32); // 32px tall logo
        }
        private static void DrawTopInfoBar()
        {
            if (!_undecoratedApplied && _applyBorderless)
            {
                Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
                _undecoratedApplied = true;
            }

            var vp = ImGui.GetMainViewport();
            const float chipH = 40f;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 4));

            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
                        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav;

            // Position: center horizontally, but width will shrink to fit content
            float posX = vp.WorkPos.X + vp.WorkSize.X * 0.5f;
            float posY = vp.WorkPos.Y + 8f;

            ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.Always, new Vector2(0.5f, 0f));
            ImGui.SetNextWindowSizeConstraints(new Vector2(0, chipH), new Vector2(9999, chipH));

            ImGui.Begin("TopBarChip", flags);

            // --- Logo ---
            EnsureLogo();
            if (_logoTex.Id != 0)
            {
                float targetH = chipH - 8f; // logo slightly smaller than bar height
                float aspect = (float)_logoTex.Width / _logoTex.Height;
                float logoW = targetH * aspect;

                float logoYOffset = (chipH - targetH) * 0.5f;
                ImGui.SetCursorPosY(logoYOffset);
                ImGui.Image((IntPtr)_logoTex.Id, new Vector2(logoW, targetH));
                ImGui.SameLine(0, 6);
            }

            // --- Text "AMBO" ---
            ImGui.PushFont(Fonts.Bold);
            var textSize = ImGui.CalcTextSize("AMBO");
            float textYOffset = (chipH - textSize.Y) * 0.5f;
            ImGui.SetCursorPosY(textYOffset);
            ImGui.TextUnformatted("AMBO");
            ImGui.PopFont();

            // --- Stats (FPS + resolution) ---
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(textYOffset);
            ImGui.TextDisabled($"· {ImGui.GetIO().Framerate:0} FPS");

            var scr = ScreenService.Current;
            int hz = Raylib.GetMonitorRefreshRate(SelectedMonitor);
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(textYOffset);
            ImGui.TextDisabled($"· {scr.W}×{scr.H} @{hz}Hz");

            // --- Close button ---
            const float rightBtnW = 28f;
            ImGui.SameLine(0, 10);
            float btnYOffset = (chipH - (chipH - 14)) * 0.5f;
            ImGui.SetCursorPosY(btnYOffset);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.15f, 0.15f, 1f));
            if (ImGui.Button("X", new Vector2(rightBtnW, chipH - 14)))
            {
                OverlayWindowApi.Quit();
            }
            ImGui.PopStyleColor(3);

            ImGui.End();
            ImGui.PopStyleVar(3);
        }

        public static void DrawInputManager()
        {
            bool fInput = BeginFold("svc.input", "Input Manager & Makcu", defaultOpen: false);
            if (!fInput) return;

            // ───────────────────────────────
            // Devices
            if (ImGui.Button("Refresh Devices"))
            {
                JobSystem.Schedule(() =>
                {
                    _inputDevices = Input.Device.EnumerateSerialDevices();
                    if (_selectedInputIndex >= _inputDevices.Count) _selectedInputIndex = -1;
                });
            }

            ImGui.Separator();
            ImGui.Text("Serial Devices:");

            string preview = (_selectedInputIndex >= 0 && _selectedInputIndex < _inputDevices.Count)
                ? _inputDevices[_selectedInputIndex].ToString()
                : "(none)";
            ImGui.SetNextItemWidth(400);
            if (ImGui.BeginCombo("##InputDevices", preview))
            {
                for (int i = 0; i < _inputDevices.Count; i++)
                {
                    bool sel = (i == _selectedInputIndex);
                    if (ImGui.Selectable(_inputDevices[i].ToString(), sel))
                        _selectedInputIndex = i;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();
            ImGui.TextDisabled("Makcu Device");

            if (ImGui.Button("Connect Makcu"))
            {
                if (_selectedInputIndex >= 0 && _selectedInputIndex < _inputDevices.Count)
                {
                    var dev = _inputDevices[_selectedInputIndex];
                    JobSystem.Schedule(() =>
                    {
                        if (Input.Device.MakcuConnect(dev.Port))
                            Console.WriteLine($"[+] Makcu connected on {dev.Port}");
                        else
                            Console.WriteLine("[-] Makcu connect failed");
                    });
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Disconnect Makcu"))
            {
                JobSystem.Schedule(() =>
                {
                    Input.Device.Disconnect();
                    Console.WriteLine("[*] Makcu disconnected");
                });
            }

            ImGui.Separator();
            ImGui.TextDisabled("Input Manager");

            if (ImGui.Button("Init InputManager"))
            {
                if (DmaMemory.Vmm is null)
                {
                    Console.WriteLine("[-] Cannot initialize InputManager: VMM not ready");
                }
                else
                {
                    JobSystem.Schedule(() =>
                    {
                        var adapter = new DmaMemory.VmmSharpExAdapter(DmaMemory.Vmm);
                        Input.InputManager.BeginInitializeWithRetries(
                            adapter,
                            TimeSpan.FromSeconds(1),
                            onComplete: (ok, err) =>
                            {
                                if (ok) Console.WriteLine("[+] InputManager ready.");
                                else Console.WriteLine($"[-] InputManager init failed: {err}");
                            });
                        _inputInitialized = true;
                    });
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Shutdown InputManager"))
            {
                JobSystem.Schedule(() =>
                {
                    Input.InputManager.Shutdown();
                    _inputInitialized = false;
                    Console.WriteLine("[*] InputManager shut down");
                });
            }

            ImGui.Separator();
            DrawStatusLight("Makcu Connected", Input.Device.connected);
            DrawStatusLight("InputManager Ready", Input.InputManager.IsReady);

            EndFold(fInput);
        }

        /// <summary>
        /// Draws a status line with Green/Orange/Red light.
        /// </summary>
        private static void DrawStatusLight(string label, bool state)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
            Vector4 col = state
                ? new Vector4(0, 0.8f, 0, 1) // Green
                : new Vector4(0.9f, 0.3f, 0.1f, 1); // Red
            dl.AddCircleFilled(new Vector2(p.X + 6, y), 5, ImGui.ColorConvertFloat4ToU32(col));
            ImGui.Dummy(new Vector2(16, ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.TextDisabled(label);
        }

               
    }
}
