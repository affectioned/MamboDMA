using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using ImGuiNET;
using static MamboDMA.OverlayWindow;
using static MamboDMA.StyleEditorUI;
using Raylib_cs;

namespace MamboDMA;

public static class OverlayUI
{
    #region Constants & Links
    private const string VmmSharpExUrl = "https://www.nuget.org/packages/VmmSharpEx";
    #endregion
    // Display: monitor selection and window style
    private static int _selectedMonitor = 0;
    private static bool _applyBorderless = true;     // borderless (undecorated) when moving to monitor
    private static bool _applyFullscreen = false;    // or toggle true full screen
    private static bool _undecoratedApplied = false; // apply once on first frame if desired
    private static string _appName = "MamboDMA";
    #region UI State (Home / Memory / About)
    // Home: process attach
    private static string _procName = "example.exe";
    private static string _status = "Idle.";
    private static bool _attempted;
    private static bool _attached;

    // Home: Input manager
    private static bool _inputInitStarted;
    private static string _inputLastMsg = "Idle";

    // Home: process list
    private static List<DmaMemory.ProcEntry> _procList = new();
    private static bool _procFetched;
    private static string _procFilter = "";
    private static int _procSelectedIndex = -1;

    // Home: Makcu
    private static bool _portsFetched;
    private static List<Input.Device.SerialDeviceInfo> _ports = new();
    private static int _selPort = -1;
    private static string _deviceQuery = "";  // user text for "by name or COM"
    private static string _makcuLastMsg = "Disconnected";

    // Memory: offsets.json
    private static bool _offsetsLoaded;
    private static List<string> _offsetNames = new();
    private static int _selOffsetIdx = -1;

    // Memory: chain builder
    enum ChainStepKind { StartBase, AddOffset, AddConst, AddScaledIndex, Deref, ReadAs }
    private enum ReturnAs { Ptr, U64, I64, I32, F32, String, Utf16 }

    private struct ChainStep
    {
        public ChainStepKind Kind;
        public string OffsetName;
        public ulong Const;        // for AddConst
        public int Index;          // for AddScaledIndex
        public ulong Stride;       // for AddScaledIndex
        public bool IndexPlusOne;  // (i+1) if true, else i
        public ReturnAs ReadType;
        public int StringMax;      // for String/Utf16
    }

    // Memory: modules
    private static List<DmaMemory.ModuleInfo> _mods = new();
    private static bool _modsFetched;
    private static string _modFilter = "";
    private static int _modSelected = -1;

    // Memory: active base for offset math
    private static ulong _activeBase = 0;
    private static string _activeBaseLabel = "Main module";
    private static readonly List<ChainStep> _chain = new();

    // Memory: last eval result
    private static string _lastEvalSummary = "";
    private static ulong _lastEvalAddress;

    // Memory: small inputs for new chain steps
    private static ulong _uiAddConst = 0x10;
    private static int _uiIndex = 0;
    private static bool _uiPlusOne = true;
    private static ulong _uiStride = 0x78;
    #endregion

    #region Public Draw
    public static void Draw()
    {
        var winFlags = StyleConfig.MakeWindowFlags();

        DrawHome(winFlags);
        DrawMemory(winFlags);
        DrawAbout(winFlags);
        DrawTopInfoBar();
        // Settings (theme editor)
        StyleEditorUI.Draw();
    }
    #endregion

    #region Window: Home
    private static void DrawHome(ImGuiWindowFlags winFlags)
    {
        ImGui.PushFont(Fonts.Bold);
        bool open = ImGui.Begin("MamboDMA · Home", winFlags);
        ImGui.PopFont();
        if (!open) { ImGui.End(); return; }

        // ── Panel: Process / DMA ──
        BeginPanel("proc_panel", "Process / DMA");
        {
            if (BeginFold("home_dma_controls", "DMA Controls", defaultOpen: true))
            {
                ImGui.TextWrapped("Initialize VMM (no attach) to load the memory map, then browse processes. You can still attach later.");

                ImGui.Spacing();

                ImGui.PushFont(Fonts.Medium);
                int clicked = UiLayout.ButtonRowAuto("Init VMM (no attach)", "Attach to Process", "Dispose VMM");
                ImGui.PopFont();

                if (clicked == 0)
                {
                    try
                    {
                        DmaMemory.InitOnly(device: "fpga", applyMMap: true);
                        _procFetched = false;
                        _status = "VMM initialized (mmap applied).";
                    }
                    catch (Exception ex) { _status = "Init error: " + ex.Message; }
                }
                else if (clicked == 1)
                {
                    using (UiLayout.PushFieldWidth(minWidth: 60, maxWidth: 220))
                        ImGui.InputText("Process", ref _procName, 256);

                    _attempted = true;
                    _attached = false;

                    if (DmaMemory.TryAttachOnce(_procName, out var err))
                    {
                        _attached = true;
                        _status = $"Attached: pid={DmaMemory.Pid} base=0x{DmaMemory.Base:X}";
                    }
                    else _status = $"Failed: {err}";
                }
                else if (clicked == 2)
                {
                    DmaMemory.Dispose();
                    _attached = false;
                    _status = "Disposed.";
                    _procFetched = false;
                    _procList.Clear();
                }

                ImGui.Spacing();
                var vmmColor = DmaMemory.IsVmmReady ? new Vector4(0, 0.8f, 0, 1) : new Vector4(1f, 0.3f, 0.2f, 1);
                DrawStatusInline(vmmColor, DmaMemory.IsVmmReady ? "VMM Ready" : "VMM Not Initialized");
                ImGui.SameLine();
                ImGui.TextColored(_attached ? new(0, 1, 0, 1) : new(1, 0.8f, 0, 1), _status);

                EndFold();
            }

            // Process list (requires VMM)
            if (BeginFold("home_proc_list", "Process List", defaultOpen: true))
            {
                if (!DmaMemory.IsVmmReady)
                {
                    ImGui.TextDisabled("Init VMM first to enumerate processes.");
                }
                else
                {
                    using (UiLayout.PushFieldWidth(40, 220))
                        ImGui.InputText("Filter (name substring)", ref _procFilter, 128);

                    ImGui.SameLine();
                    if (ImGui.Button("Refresh")) TryFetchProcs();
                    if (!_procFetched) TryFetchProcs();

                    ImGui.BeginChild("proc_list_child", new Vector2(0, 240), ImGuiChildFlags.None);
                    int shown = 0;
                    for (int i = 0; i < _procList.Count; i++)
                    {
                        var p = _procList[i];
                        if (!string.IsNullOrWhiteSpace(_procFilter) &&
                            p.Name?.IndexOf(_procFilter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        bool selected = (i == _procSelectedIndex);
                        if (ImGui.Selectable($"{p.Pid,6}  {p.Name}{(p.IsWow64 ? "  (Wow64)" : "")}", selected))
                            _procSelectedIndex = i;
                        shown++;
                    }
                    if (shown == 0) ImGui.TextDisabled("(no processes match filter)");
                    ImGui.EndChild();

                    if (_procSelectedIndex >= 0 && _procSelectedIndex < _procList.Count)
                    {
                        ImGui.Spacing();
                        ImGui.TextDisabled("Attach to selected process:");
                        ImGui.SameLine();
                        if (ImGui.Button("Attach"))
                        {
                            var sel = _procList[_procSelectedIndex];
                            _procName = sel.Name;
                            _attempted = true;
                            _attached = false;
                            if (DmaMemory.TryAttachOnce(sel.Name, out var err))
                            {
                                _attached = true;
                                _status = $"Attached: pid={DmaMemory.Pid} base=0x{DmaMemory.Base:X}";
                            }
                            else _status = $"Failed: {err}";
                        }
                    }
                }
                EndFold();
            }
        }
        EndPanel();

        // ── Panel: Input Manager ──
        BeginPanel("input_panel", "Input Manager");
        {
            if (BeginFold("home_input_mgr", "Input Manager Controls", defaultOpen: true))
            {
                var inputStatus = GetInputStatus();
                DrawStatusInline(inputStatus.color, inputStatus.label);

                UiLayout.SameLineIfFits(UiLayout.CalcButtonSize("Init Input Manager").X + 20);
                bool canInit = DmaMemory.IsVmmReady && !_inputInitStarted;
                if (!canInit)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Init Input Manager");
                    ImGui.EndDisabled();

                    if (!DmaMemory.IsVmmReady && ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Init VMM first.");
                        ImGui.EndTooltip();
                    }
                }
                else
                {
                    if (ImGui.Button("Init Input Manager"))
                    {
                        _inputInitStarted = true;
                        _inputLastMsg = "Starting…";
                        try
                        {
                            var adapter = new DmaMemory.VmmSharpExAdapter(DmaMemory.Vmm);
                            Input.InputManager.BeginInitializeWithRetries(
                                adapter,
                                TimeSpan.FromSeconds(2),
                                default,
                                (ok, e) =>
                                {
                                    _inputLastMsg = ok ? "Ready" : ("Init failed: " + (e ?? "unknown"));
                                    Console.WriteLine(ok ? "[INPUT] Ready." : "[INPUT] init failed: " + (e ?? "unknown"));
                                });
                        }
                        catch (Exception ex)
                        {
                            _inputLastMsg = "Boot error: " + ex.Message;
                            Console.WriteLine("[INPUT] boot error: " + ex.Message);
                        }
                    }
                }
                EndFold();
            }
        }
        EndPanel();

        // ── Panel: Makcu ──
        BeginPanel("makcu_panel", "Makcu");
        {
            if (BeginFold("home_makcu", "Makcu Controls", defaultOpen: true))
            {
                DrawStatusInline(Input.Device.connected ? new Vector4(0f, 0.8f, 0f, 1f) : new Vector4(1f, 0.3f, 0.2f, 1f),
                                 Input.Device.connected ? (Input.Device.version ?? "Connected") : "Disconnected");

                if (!_portsFetched) RefreshPorts();

                using (UiLayout.PushFieldWidth(60, 220))
                {
                    string preview = _selPort >= 0 && _selPort < _ports.Count
                        ? $"{_ports[_selPort].Port} — {_ports[_selPort].Name}"
                        : "(choose detected device)";
                    if (ImGui.BeginCombo("Detected", preview))
                    {
                        for (int i = 0; i < _ports.Count; i++)
                        {
                            bool selected = i == _selPort;
                            var item = _ports[i];
                            if (ImGui.Selectable($"{item.Port} — {item.Name}", selected))
                                _selPort = i;
                            if (selected) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Refresh Devices")) RefreshPorts();

                if (ImGui.Button("Init Makcu (Selected)"))
                {
                    if (_selPort >= 0 && _selPort < _ports.Count)
                    {
                        var com = _ports[_selPort].Port;
                        _makcuLastMsg = $"Connecting {com}…";
                        bool ok = Input.Device.MakcuConnect(com);
                        _makcuLastMsg = ok ? $"Connected {com}" : $"Failed {com}";
                    }
                }
                ImGui.SameLine();
                ImGui.TextDisabled(_makcuLastMsg);

                using (UiLayout.PushFieldWidth(40, 220))
                    ImGui.InputText("Device (COM or name)", ref _deviceQuery, 200);

                int which = UiLayout.ButtonRowAuto("Init by Name/COM", "Auto");
                if (which == 0)
                {
                    if (!string.IsNullOrWhiteSpace(_deviceQuery))
                    {
                        _makcuLastMsg = $"Connecting '{_deviceQuery}'…";
                        bool ok = Input.Device.MakcuConnect(_deviceQuery);
                        _makcuLastMsg = ok ? $"Connected {_deviceQuery}" : $"Failed '{_deviceQuery}'";
                    }
                }
                else if (which == 1)
                {
                    _makcuLastMsg = "Auto-connect…";
                    bool ok = Input.Device.AutoConnectMakcu();
                    _makcuLastMsg = ok ? "Auto: Connected" : "Auto: Not found";
                }
                EndFold();
            }
        }
        EndPanel();
        // ── Panel: Display / Monitors ──
        BeginPanel("display_panel", "Display");
        {
            if (BeginFold("home_display", "Monitor & Window", defaultOpen: true))
            {
                // Ensure we have at least one monitor
                int monCount = Raylib.GetMonitorCount();
                if (_selectedMonitor >= monCount) _selectedMonitor = monCount > 0 ? monCount - 1 : 0;
        
                // Build monitor labels
                var labels = new List<string>(monCount);
                for (int i = 0; i < monCount; i++)
                {
                    int w = Raylib.GetMonitorWidth(i);
                    int h = Raylib.GetMonitorHeight(i);
                    labels.Add($"Monitor {i}  ({w}×{h})  @{Raylib.GetMonitorRefreshRate(i)}Hz");
                }
        
                // Combo
                using (UiLayout.PushFieldWidth(260, 420))
                {
                    string preview = (monCount > 0) ? labels[_selectedMonitor] : "(no monitors)";
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
        
                // Options
                ImGui.Checkbox("Borderless (undecorated)", ref _applyBorderless);
                ImGui.SameLine();
                ImGui.Checkbox("Fullscreen", ref _applyFullscreen);
        
                // Action buttons
                int b = UiLayout.ButtonRowAuto("Apply", "Center Window", "Restore Decorations");
                if (b == 0) ApplyMonitorSelection();
                else if (b == 1) CenterOnMonitor(_selectedMonitor);
                else if (b == 2) RestoreDecorations();
        
                ImGui.Separator();
                ImGui.TextDisabled("Tip: Borderless + sizing to monitor gives a clean fullscreen feel without Alt-Tab quirks.");
                EndFold();
            }
        }
        EndPanel();
        ImGui.End();
    }
    #endregion

    #region Window: Memory
    private static void DrawMemory(ImGuiWindowFlags winFlags)
    {
        ImGui.PushFont(Fonts.Bold);
        bool memOpen = ImGui.Begin("MamboDMA · Memory", winFlags);
        ImGui.PopFont();
        if (!memOpen) { ImGui.End(); return; }

        if (!DmaMemory.IsAttached)
        {
            ImGui.TextDisabled("Attach to a process to use the memory tools.");
            ImGui.End();
            return;
        }

        // Load offsets.json once
        if (!_offsetsLoaded)
        {
            Offsets.EnsureExistsWithExample();
            _offsetsLoaded = Offsets.Load();
            _offsetNames = Offsets.Names().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            if (_offsetNames.Count > 0) _selOffsetIdx = 0;
        }

        // Top info
        if (BeginFold("mem_top", "Process Info", defaultOpen: true))
        {
            ImGui.BulletText($"PID:  {DmaMemory.Pid}");
            ImGui.BulletText($"Main Base: 0x{DmaMemory.Base:X}");
            if (_activeBase == 0) { _activeBase = DmaMemory.Base; _activeBaseLabel = "Main module"; }
            ImGui.BulletText($"Active Base: 0x{_activeBase:X}  ({_activeBaseLabel})");
            EndFold();
        }

        ImGui.Separator();

        // Modules
        if (BeginFold("mem_modules", "Modules (pick Active Base)", defaultOpen: true))
        {
            using (UiLayout.PushFieldWidth(260, 420))
                ImGui.InputText("Filter (e.g. client.dll)", ref _modFilter, 128);

            ImGui.SameLine();
            if (ImGui.Button("Refresh Modules")) _modsFetched = false;

            if (!_modsFetched) TryFetchModules();

            ImGui.BeginChild("mods_child", new Vector2(0, 160), ImGuiChildFlags.None);
            int shownMods = 0;
            for (int i = 0; i < _mods.Count; i++)
            {
                var m = _mods[i];
                if (!string.IsNullOrWhiteSpace(_modFilter) &&
                    m.Name.IndexOf(_modFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    m.FullName.IndexOf(_modFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool sel = (i == _modSelected);
                if (ImGui.Selectable($"{m.Name,-28}  Base=0x{m.Base:X}  Size=0x{m.Size:X}", sel))
                    _modSelected = i;
                shownMods++;
            }
            if (shownMods == 0) ImGui.TextDisabled("(no modules match filter)");
            ImGui.EndChild();

            if (ImGui.Button("Use Main Module Base"))
            {
                _activeBase = DmaMemory.Base;
                _activeBaseLabel = "Main module";
            }
            ImGui.SameLine();
            if (ImGui.Button("Use Selected Module Base"))
            {
                if (_modSelected >= 0 && _modSelected < _mods.Count)
                {
                    var m = _mods[_modSelected];
                    _activeBase = m.Base;
                    _activeBaseLabel = m.Name;
                }
            }
            EndFold();
        }

        ImGui.Separator();

        // Offsets list
        if (BeginFold("mem_offsets", "Offsets (offsets.json)", defaultOpen: true))
        {
            ImGui.BeginChild("offsets_child", new Vector2(350, 220), ImGuiChildFlags.None);
            if (!_offsetsLoaded)
            {
                ImGui.TextDisabled("Failed to load offsets.json.");
                if (ImGui.Button("Retry Load")) _offsetsLoaded = Offsets.Load();
            }
            else
            {
                for (int i = 0; i < _offsetNames.Count; i++)
                {
                    string n = _offsetNames[i];
                    bool selected = i == _selOffsetIdx;
                    if (ImGui.Selectable(n, selected)) _selOffsetIdx = i;
                }
            }
            ImGui.EndChild();
            EndFold();
        }

        ImGui.Separator();

        // Chain Builder
        if (BeginFold("mem_chain", "Read Chain Builder", defaultOpen: true))
        {
            ImGui.TextDisabled("Build a chain: Base → +Offset/Const/(i*stride) → Deref → ReturnAs");

            // Row: Start / +Offset / Deref / Return
            if (ImGui.Button("Start: Base")) _chain.Add(new ChainStep { Kind = ChainStepKind.StartBase });

            ImGui.SameLine();
            if (ImGui.Button("+ Offset (selected)"))
            {
                if (_selOffsetIdx >= 0 && _selOffsetIdx < _offsetNames.Count)
                    _chain.Add(new ChainStep { Kind = ChainStepKind.AddOffset, OffsetName = _offsetNames[_selOffsetIdx] });
            }

            ImGui.SameLine();
            if (ImGui.Button("→ Deref to Ptr")) _chain.Add(new ChainStep { Kind = ChainStepKind.Deref });

            ImGui.SameLine();
            using (UiLayout.PushFieldWidth(120, 180))
            {
                if (ImGui.BeginCombo("Return As", "Choose..."))
                {
                    foreach (ReturnAs t in Enum.GetValues(typeof(ReturnAs)))
                        if (ImGui.Selectable(t.ToString()))
                            _chain.Add(new ChainStep { Kind = ChainStepKind.ReadAs, ReadType = t, StringMax = 64 });
                    ImGui.EndCombo();
                }
            }

            // Extra: +Const and +(i)*stride
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("Index/stride helpers:");

            // + Const
            ImGui.SetNextItemWidth(140);
            InputU64("const", ref _uiAddConst);
            ImGui.SameLine();
            if (ImGui.Button("+ Const"))
                _chain.Add(new ChainStep { Kind = ChainStepKind.AddConst, Const = _uiAddConst });

            // (i [+1]) * stride
            ImGui.SameLine();
            ImGui.SetNextItemWidth(60);
            ImGui.InputInt("i", ref _uiIndex);
            ImGui.SameLine();
            ImGui.Checkbox("+1", ref _uiPlusOne);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            InputU64("stride", ref _uiStride);
            ImGui.SameLine();
            if (ImGui.Button($"+ (i{(_uiPlusOne ? "+1" : "")})*stride"))
                _chain.Add(new ChainStep { Kind = ChainStepKind.AddScaledIndex, Index = _uiIndex, IndexPlusOne = _uiPlusOne, Stride = _uiStride });

            // If last step is "ReadAs String/Utf16", allow editing max
            if (_chain.Count > 0)
            {
                var last = _chain[^1];
                if (last.Kind == ChainStepKind.ReadAs && (last.ReadType == ReturnAs.String || last.ReadType == ReturnAs.Utf16))
                {
                    int mx = last.StringMax <= 0 ? 64 : last.StringMax;
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.InputInt("max", ref mx))
                    {
                        if (mx < 1) mx = 1;
                        if (mx > 4096) mx = 4096;
                        last.StringMax = mx;
                        _chain[^1] = last;
                    }
                }
            }

            ImGui.Spacing();

            // Chain steps list
            ImGui.BeginChild("chain_steps", new Vector2(0, 180), ImGuiChildFlags.None);
            for (int i = 0; i < _chain.Count; i++)
            {
                var s = _chain[i];
                ImGui.Bullet();
                switch (s.Kind)
                {
                    case ChainStepKind.StartBase:
                        ImGui.TextUnformatted($"Start: Base (0x{_activeBase:X} · {_activeBaseLabel})"); break;
                    case ChainStepKind.AddOffset:
                        ImGui.TextUnformatted($"+ Offset: {s.OffsetName}"); break;
                    case ChainStepKind.AddConst:
                        ImGui.TextUnformatted($"+ Const: 0x{s.Const:X}"); break;
                    case ChainStepKind.AddScaledIndex:
                        ImGui.TextUnformatted($"+ ({(s.IndexPlusOne ? "i+1" : "i")}={(s.IndexPlusOne ? s.Index + 1 : s.Index)}) * 0x{s.Stride:X}"); break;
                    case ChainStepKind.Deref:
                        ImGui.TextUnformatted("→ Deref to Ptr"); break;
                    case ChainStepKind.ReadAs:
                        ImGui.TextUnformatted($"Return As: {s.ReadType}{(s.ReadType == ReturnAs.String || s.ReadType == ReturnAs.Utf16 ? $" (max {s.StringMax})" : "")}"); break;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##{i}")) { _chain.RemoveAt(i); i--; }
            }
            ImGui.EndChild();

            // Evaluate/Clear
            ImGui.Spacing();
            if (ImGui.Button("Evaluate")) EvaluateChain(out _lastEvalSummary, out _lastEvalAddress);
            ImGui.SameLine();
            if (ImGui.Button("Clear Chain")) { _chain.Clear(); _lastEvalSummary = ""; }

            EndFold();
        }

        ImGui.Separator();

        // Results
        if (BeginFold("mem_results", "Results", defaultOpen: true))
        {
            ImGui.Text("Result:");
            if (string.IsNullOrEmpty(_lastEvalSummary)) ImGui.TextDisabled("(none)");
            else ImGui.TextWrapped(_lastEvalSummary);

            ImGui.Separator();

            // Example scatter demo button (kept from your snippet)
            if (ImGui.Button("Execute Scatter (Map Name demo)"))
            {
                ulong globalVars = 0;
                ulong mapNamePtr = 0;

                DmaMemory.ScatterRound(rd =>
                {
                    rd[0].AddValueEntry<ulong>(0, _activeBase + 0x1BD6150); // dwGlobalVars
                    rd[0].Completed += (_, cb) => cb.TryGetValue<ulong>(0, out globalVars);
                }, useCache: false);

                if (globalVars != 0)
                {
                    DmaMemory.ScatterRound(rd2 =>
                    {
                        rd2[0].AddValueEntry<ulong>(0, globalVars + 0x0188); // CurrentMap
                        rd2[0].Completed += (_, cb) => cb.TryGetValue<ulong>(0, out mapNamePtr);
                    }, useCache: false);
                }

                if (mapNamePtr != 0)
                {
                    string mapName = DmaMemory.ReadAsciiZ(mapNamePtr, 64);
                    Console.WriteLine($"MapName: {mapName}");
                    _lastEvalSummary = $"Scatter demo: MapName = \"{mapName}\"";
                }
            }

            EndFold();
        }

        ImGui.End();
    }
    #endregion

    #region Window: About
    private static void DrawAbout(ImGuiWindowFlags winFlags)
    {
        ImGui.PushFont(Fonts.Bold);
        bool aboutOpen = ImGui.Begin("MamboDMA · About", winFlags);
        ImGui.PopFont();
        if (!aboutOpen) { ImGui.End(); return; }

        if (BeginFold("about_tabs", "About (Tabs)", defaultOpen: true))
        {
            if (ImGui.BeginTabBar("AboutTabs"))
            {
                ImGui.PushFont(Fonts.Bold);
                bool infoTabOpen = ImGui.BeginTabItem("Info");
                ImGui.PopFont();
                if (infoTabOpen)
                {
                    ImGui.TextWrapped("MamboDMA example | ImGui.NET + Raylib + rlImGui_cs + VmmSharpEx. Docking enabled.");
                    ImGui.EndTabItem();
                }

                ImGui.PushFont(Fonts.Bold);
                bool creditsTabOpen = ImGui.BeginTabItem("Credits");
                ImGui.PopFont();
                if (creditsTabOpen)
                {
                    ImGui.Text("Thanks to Lone for his release of");
                    ImGui.SameLine();
                    LinkLabel("VmmSharpEx", VmmSharpExUrl);
                    ImGui.SameLine();
                    ImGui.Text("<3");
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
            EndFold();
        }

        ImGui.End();
    }
    #endregion
    #region TopBar/InfoBar
    private static void DrawTopInfoBar()
    {
        // Apply undecorated once if requested
        if (!_undecoratedApplied && _applyBorderless)
        {
            Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
            _undecoratedApplied = true;
        }

        var vp = ImGui.GetMainViewport();
        // Fixed chip width so we can center it easily; tweak to taste.
        const float chipW = 420f;
        const float chipH = 36f;

        var posX = vp.WorkPos.X + (vp.WorkSize.X - chipW) * 0.5f;
        var posY = vp.WorkPos.Y + 8f;

        ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(chipW, chipH), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));

        var flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoNav;

        ImGui.Begin("TopBarChip", flags);

        // Left: logo (placeholder circle)
        {
            var dl = ImGui.GetWindowDrawList();
            var pMin = ImGui.GetCursorScreenPos();
            var center = new Vector2(pMin.X + 12, pMin.Y + chipH * 0.5f);
            dl.AddCircleFilled(center, 8f, ImGui.GetColorU32(new Vector4(0.2f, 0.6f, 1f, 1f)));
            ImGui.Dummy(new Vector2(26, chipH - 12)); // reserve space
            ImGui.SameLine();
        }

        // Middle: App name + FPS (center-ish)
        ImGui.PushFont(Fonts.Bold);
        ImGui.TextUnformatted(_appName);
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextDisabled($"·  {ImGui.GetIO().Framerate:0} FPS");

        // Right: Close “X”
        float rightBtnW = 26f;
        ImGui.SameLine();
        // push to right edge
        float avail = ImGui.GetContentRegionAvail().X;
        if (avail > rightBtnW) ImGui.Dummy(new Vector2(avail - rightBtnW, 1));
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.3f, 0.3f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.15f, 0.15f, 1f));
        if (ImGui.Button("X", new Vector2(rightBtnW, chipH - 14)))
        {
            // pick your close behavior
            try { Raylib.CloseWindow(); } catch { Environment.Exit(0); }
        }
        ImGui.PopStyleColor(3);

        ImGui.End();
        ImGui.PopStyleVar(3);
    }
    #endregion
    #region Monitor Helpers
    private static void ApplyMonitorSelection()
    {
        int monCount = Raylib.GetMonitorCount();
        if (monCount <= 0) return;

        int mon = Math.Clamp(_selectedMonitor, 0, monCount - 1);

        // Choose your style:
        if (_applyBorderless)
        {
            Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
            _undecoratedApplied = true;
        }
        else
        {
            Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow);
            _undecoratedApplied = false;
        }

        // Move + size to monitor bounds
        var pos = Raylib.GetMonitorPosition(mon);
        int w = Raylib.GetMonitorWidth(mon);
        int h = Raylib.GetMonitorHeight(mon);

        // Make sure we’re windowed before sizing (avoids some driver quirks)
        if (Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();

        Raylib.SetWindowPosition((int)pos.X, (int)pos.Y);
        Raylib.SetWindowSize(w, h);

        // Optional: true fullscreen
        if (_applyFullscreen && !Raylib.IsWindowFullscreen())
            Raylib.ToggleFullscreen();
    }

    private static void CenterOnMonitor(int mon)
    {
        int monCount = Raylib.GetMonitorCount();
        if (monCount <= 0) return;

        mon = Math.Clamp(mon, 0, monCount - 1);
        var pos = Raylib.GetMonitorPosition(mon);
        int mw = Raylib.GetMonitorWidth(mon);
        int mh = Raylib.GetMonitorHeight(mon);

        int ww = Raylib.GetScreenWidth();
        int wh = Raylib.GetScreenHeight();

        int x = (int)pos.X + (mw - ww) / 2;
        int y = (int)pos.Y + (mh - wh) / 2;
        Raylib.SetWindowPosition(x, y);
    }

    private static void RestoreDecorations()
    {
        Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow);
        _undecoratedApplied = false;

        if (Raylib.IsWindowFullscreen())
            Raylib.ToggleFullscreen();
    }
    #endregion
    #region Panel Helpers
    private static void BeginPanel(string id, string title)
    {
        ImGui.PushFont(Fonts.Bold);
        //ImGui.TextUnformatted(title);
        ImGui.PopFont();

        ImGui.BeginChild(id, new Vector2(0, 0), ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.None);
        ImGui.Spacing();
    }

    private static void EndPanel()
    {
        ImGui.Spacing();
        ImGui.EndChild();
        ImGui.Dummy(new Vector2(0, 6)); // gap between panels
    }

    /// <summary>
    /// Foldable section: CollapsingHeader + bordered child.
    /// </summary>
    private static bool BeginFold(string id, string title, bool defaultOpen = false)
    {
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth
                  | (defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : 0);
        bool open = ImGui.CollapsingHeader(title, flags);
        if (open)
        {
            ImGui.PushID(id);
            ImGui.Indent();
            ImGui.Spacing();
        }
        return open;
    }

    private static void EndFold()
    {
        ImGui.Spacing();
        ImGui.Unindent();
        ImGui.PopID();
    }
    #endregion

    #region Misc Helpers
    private static (Vector4 color, string label) GetInputStatus()
    {
        if (Input.InputManager.IsReady) return (new Vector4(0f, 0.8f, 0f, 1f), "Ready");
        if (Input.InputManager.IsInitializing) return (new Vector4(1.0f, 0.6f, 0f, 1f), "Initializing…");
        if (_inputInitStarted) return (new Vector4(1f, 0.3f, 0.2f, 1f), _inputLastMsg);
        return (new Vector4(0.7f, 0.7f, 0.7f, 1f), _inputLastMsg);
    }

    private static unsafe bool InputU64(string label, ref ulong value, string format = "0x%llX")
    {
        fixed (ulong* p = &value)
            return ImGui.InputScalar(label, ImGuiDataType.U64, (nint)p, IntPtr.Zero, IntPtr.Zero, format);
    }

    private static void DrawStatusInline(Vector4 color, string caption)
    {
        DrawStatusDot(color);
        ImGui.SameLine();
        ImGui.TextDisabled(caption);
    }

    private static void DrawStatusDot(Vector4 color, float radius = 5f)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
        dl.AddCircleFilled(new Vector2(p.X + radius, y), radius, ImGui.ColorConvertFloat4ToU32(color));
        ImGui.Dummy(new Vector2(radius * 2 + 2, ImGui.GetTextLineHeight()));
    }

    private static void LinkLabel(string text, string url)
    {
        var linkColor = new Vector4(0.25f, 0.55f, 1.0f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Text, linkColor);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), ImGui.GetColorU32(ImGuiCol.Text), 1.0f);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(url);
            ImGui.EndTooltip();
        }
        if (ImGui.IsItemClicked())
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }

    private static void TryFetchModules()
    {
        try
        {
            _mods = DmaMemory.GetModules();
            _modsFetched = true;
            if (_mods.Count == 0) _modSelected = -1;
            else if (_modSelected < 0 || _modSelected >= _mods.Count) _modSelected = 0;
        }
        catch (Exception ex)
        {
            _mods = new();
            _modSelected = -1;
            _modsFetched = true;
            _status = "Module list error: " + ex.Message;
        }
    }

    private static void TryFetchProcs()
    {
        try
        {
            _procList = DmaMemory.GetProcessList();
            _procFetched = true;
            if (_procList.Count == 0) _procSelectedIndex = -1;
            else if (_procSelectedIndex < 0 || _procSelectedIndex >= _procList.Count) _procSelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _procList = new();
            _procSelectedIndex = -1;
            _procFetched = true;
            _status = "Proc list error: " + ex.Message;
        }
    }

    private static bool TryGetOffsetValue(string name, out ulong val) => Offsets.TryGet(name, out val);
    private static void RefreshPorts()
    {
        try
        {
            _ports = Input.Device.EnumerateSerialDevices();
            _portsFetched = true;
            if (_ports.Count == 0) _selPort = -1;
            else if (_selPort < 0 || _selPort >= _ports.Count) _selPort = 0;
        }
        catch
        {
            _ports = new();
            _selPort = -1;
            _portsFetched = true;
        }
    }     
    #endregion

    #region Chain Evaluator
    private static void EvaluateChain(out string desc, out ulong lastAddr)
    {
        desc = "";
        lastAddr = 0;

        if (_chain.Count == 0)
        {
            desc = "Empty chain.";
            return;
        }

        ulong cur = 0;
        bool haveStart = false;
        string log = "";

        for (int i = 0; i < _chain.Count; i++)
        {
            var s = _chain[i];
            switch (s.Kind)
            {
                case ChainStepKind.StartBase:
                    cur = (_activeBase != 0 ? _activeBase : DmaMemory.Base);
                    haveStart = true;
                    log += $"Start = Base (0x{cur:X})\n";
                    break;

                case ChainStepKind.AddOffset:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    if (!TryGetOffsetValue(s.OffsetName, out var ofs)) { desc = $"Unknown offset '{s.OffsetName}'."; return; }
                    cur += ofs;
                    log += $"+ {s.OffsetName} (0x{ofs:X}) = 0x{cur:X}\n";
                    break;

                case ChainStepKind.AddConst:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    cur += s.Const;
                    log += $"+ 0x{s.Const:X} = 0x{cur:X}\n";
                    break;

                case ChainStepKind.AddScaledIndex:
                    {
                        if (!haveStart) { desc = "Chain must start with Base."; return; }
                        long ii = s.IndexPlusOne ? (long)s.Index + 1 : s.Index;
                        ulong add = (ulong)ii * s.Stride;
                        cur += add;
                        log += $"+ ({(s.IndexPlusOne ? "i+1" : "i")}={ii})*0x{s.Stride:X} = +0x{add:X} -> 0x{cur:X}\n";
                        break;
                    }

                case ChainStepKind.Deref:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    if (!DmaMemory.Read(cur, out ulong ptr))
                    {
                        desc = $"Deref @ 0x{cur:X} failed.";
                        return;
                    }
                    log += $"*[0x{cur:X}] => 0x{ptr:X}\n";
                    cur = ptr;
                    break;

                case ChainStepKind.ReadAs:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    lastAddr = cur;

                    switch (s.ReadType)
                    {
                        case ReturnAs.Ptr:
                            desc = log + $"Return Ptr @ 0x{cur:X}";
                            return;

                        case ReturnAs.U64:
                            if (DmaMemory.Read(cur, out ulong u64))
                                desc = log + $"u64 @ 0x{cur:X} = {u64} (0x{u64:X})";
                            else desc = log + $"u64 read @ 0x{cur:X} failed.";
                            return;

                        case ReturnAs.I64:
                            if (DmaMemory.Read(cur, out long i64))
                                desc = log + $"i64 @ 0x{cur:X} = {i64} (0x{i64:X})";
                            else desc = log + $"i64 read @ 0x{cur:X} failed.";
                            return;

                        case ReturnAs.I32:
                            if (DmaMemory.Read(cur, out int i32))
                                desc = log + $"i32 @ 0x{cur:X} = {i32} (0x{i32:X})";
                            else desc = log + $"i32 read @ 0x{cur:X} failed.";
                            return;

                        case ReturnAs.F32:
                            if (DmaMemory.Read(cur, out float f32))
                                desc = log + $"f32 @ 0x{cur:X} = {f32}";
                            else desc = log + $"f32 read @ 0x{cur:X} failed.";
                            return;

                        case ReturnAs.Utf16:
                            {
                                var smax = Math.Max(1, Math.Min(4096, s.StringMax));
                                var text = DmaMemory.ReadUtf16Z(cur, smax) ?? "";
                                desc = log + $"utf16 @ 0x{cur:X} = \"{text}\"";
                                return;
                            }

                        case ReturnAs.String:
                            {
                                var smax = Math.Max(1, Math.Min(4096, s.StringMax <= 0 ? 64 : s.StringMax));
                                var text = DmaMemory.ReadAsciiZ(cur, smax) ?? "";
                                desc = log + $"string @ 0x{cur:X} = \"{text}\"";
                                return;
                            }
                    }
                    break;
            }
        }

        // If no ReadAs, show final address
        lastAddr = cur;
        desc = log + $"Final Addr = 0x{cur:X} (add a 'Return As' step to read)";
    }
    #endregion
 
}
