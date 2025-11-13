// MamboDMA/Games/GameSelector.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImGuiNET;
using MamboDMA.Gui;
using MamboDMA.Input;
using MamboDMA.Services;
using Raylib_cs;
using static MamboDMA.Misc;
using static MamboDMA.OverlayWindow;
using static MamboDMA.StyleEditorUI;

namespace MamboDMA.Games
{

    // ---------------------------------------------------------------------
    // Keybind config model
    // ---------------------------------------------------------------------
    public enum KeybindMode { OnPress, OnRelease, WhileDown }

    public enum KeybindAction
    {
        // UI
        ToggleServiceControl,
        ToggleHomeWindow,
        ToggleTopBar,
        ToggleKeybindsWindow,
        ToggleAllMenus,

        // VMM / processes
        InitVmm,
        DisposeVmm,
        RefreshProcesses,
        AttachSelectedProcess,

        // Window helpers
        CenterWindow,
        RestoreDecorations
    }

    public sealed class KeybindEntry
    {
        public string Name { get; set; } = "New Bind";
        public int Vk { get; set; }
        public bool Ctrl { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public KeybindMode Mode { get; set; } = KeybindMode.OnPress;

        // NOTE: Action is a string so we can store global *or* game-specific actions.
        public string Action { get; set; } = nameof(KeybindAction.ToggleServiceControl);

        public override string ToString()
        {
            string mods = (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "");
            return $"{Name}: {mods}{VkNames.Name(Vk)} ({Mode}) → {Action}";
        }

        public bool MatchesModifiers(bool ctrl, bool shift, bool alt)
            => ctrl == Ctrl && shift == Shift && alt == Alt;
    }

    public sealed class KeybindProfile
    {
        public string ProfileName { get; set; } = "Default";
        public string Category { get; set; } = "Default"; // Default, ABI, Reforger, etc.
        public List<KeybindEntry> Binds { get; set; } = new();
    }

    internal static class KeybindRegistry
    {
        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Configs");

        public static readonly JsonSerializerOptions JOpt = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public sealed class ProfileRef
        {
            public string Path { get; init; } = "";
            public string Display { get; init; } = "";
            public string Category { get; init; } = "Default";
        }

        public static List<ProfileRef> Discover()
        {
            var list = new List<ProfileRef>();
            if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);

            foreach (var f in Directory.EnumerateFiles(Root, "*.keybinds.json", SearchOption.TopDirectoryOnly))
                list.Add(ToRef(f, "Default"));

            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var category = System.IO.Path.GetFileName(dir);
                foreach (var f in Directory.EnumerateFiles(dir, "*.keybinds.json", SearchOption.AllDirectories))
                    list.Add(ToRef(f, category));
            }

            if (list.Count == 0)
            {
                var defPath = System.IO.Path.Combine(Root, "default.keybinds.json");
                var def = new KeybindProfile
                {
                    ProfileName = "Default Keybinds",
                    Category = "Default",
                    Binds = new List<KeybindEntry>
                    {
                        new KeybindEntry { Name="Toggle Service Control", Vk=VK.F1, Action=nameof(KeybindAction.ToggleServiceControl), Mode=KeybindMode.OnPress },
                        new KeybindEntry { Name="Toggle Home Window",    Vk=VK.F2, Action=nameof(KeybindAction.ToggleHomeWindow),     Mode=KeybindMode.OnPress },
                        new KeybindEntry { Name="Toggle Keybinds",       Vk=VK.F6, Action=nameof(KeybindAction.ToggleKeybindsWindow), Mode=KeybindMode.OnPress },
                        new KeybindEntry { Name="Toggle Top Bar",        Vk=VK.F9, Action=nameof(KeybindAction.ToggleTopBar),         Mode=KeybindMode.OnPress },
                        new KeybindEntry { Name="Hide/Show All Menus",   Vk=0x79 /* F10 */, Action=nameof(KeybindAction.ToggleAllMenus), Mode=KeybindMode.OnPress },
                    }
                };
                Save(defPath, def);
                list.Add(ToRef(defPath, "Default"));
            }

            list = list.OrderBy(r => r.Category != "Default").ThenBy(r => r.Category).ThenBy(r => r.Display).ToList();
            return list;
        }

        private static ProfileRef ToRef(string path, string category)
        {
            var disp = System.IO.Path.GetFileNameWithoutExtension(path);
            return new ProfileRef { Path = path, Display = $"{category}: {disp}", Category = category };
        }

        public static KeybindProfile Load(string path)
        {
            try
            {
                var txt = File.ReadAllText(path);
                var prof = JsonSerializer.Deserialize<KeybindProfile>(txt, JOpt) ?? new KeybindProfile();
                prof.ProfileName ??= System.IO.Path.GetFileNameWithoutExtension(path);
                prof.Category ??= "Default";
                prof.Binds ??= new List<KeybindEntry>();

                // very small migration safety
                foreach (var b in prof.Binds)
                    b.Action ??= nameof(KeybindAction.ToggleServiceControl);

                return prof;
            }
            catch
            {
                return new KeybindProfile { ProfileName = "Broken Profile", Category = "Default" };
            }
        }

        public static void Save(string path, KeybindProfile profile)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(profile, JOpt));
        }
    }

    // ---------------------------------------------------------------------
    // GameSelector (with separate Keybinds window + Hide-All-Menus)
    // ---------------------------------------------------------------------
    public static class GameSelector
    {
        // Global flags
        public static bool VmmInnit { get; private set; }
        public static bool AttachedToProccess { get; private set; }

        private static bool _loadedLastGame = false;
        private static readonly string _lastGamePath =
            System.IO.Path.Combine(AppContext.BaseDirectory, "last_game.txt");
        private static int _selectedMonitor = 0;
        private static bool _applyBorderless = false;
        private static bool _applyFullscreen = false;
        private static bool _undecoratedApplied = false;
        static bool _uiUseVsync = false;
        static int _uiFpsCap = 144;

        private static readonly string[] _globalActions =
            Enum.GetNames(typeof(KeybindAction));

        // Process/Module UI state
        private static int _procSelectedIndex = -1;
        private static string _procFilter = string.Empty;
        private static string _modFilter = string.Empty;

        // transitions
        private static bool _prevVmmInnit = false;
        private static bool _prevAttached = false;

        // Windows visibility
        private static bool _serviceControlVisible = true;
        private static bool _homeVisible = true;
        private static bool _keybindsVisible = true; // dedicated window

        // Global UI hide (menus off, overlays/ESP still run)
        private static bool _allMenusHidden = false;

        // Top bar
        private static bool _showTopInfoBar = true;
        private static bool _topBarLocked = false;
        private static bool _topBarPrimed = false;

        // Input
        private static int _selectedInputIndex = -1;
        private static List<MamboDMA.Input.Device.SerialDeviceInfo> _inputDevices = new();
        private static bool _inputInitialized = false;

        // Keybinds state
        private static List<KeybindRegistry.ProfileRef> _profiles = new();
        private static int _selectedProfileIndex = 0;
        private static KeybindProfile _activeProfile = new();
        private static string _activeProfilePath = "";
        private static bool _wantsRebind = false;   // editor capture flag
        private static int _rebindVk = 0;
        private static bool _rebindCtrl, _rebindShift, _rebindAlt;
        private static int _editingIndex = -1;      // which bind we edit (-1 = adding)
        private static string _newBindName = "New Bind";
        private static string _editActionName = nameof(KeybindAction.ToggleServiceControl);
        private static KeybindMode _newBindMode = KeybindMode.OnPress;

        // cache hotkey registrations to avoid duplicates
        private static bool _hotkeysRegistered = false;

        public static int SelectedMonitor => _selectedMonitor;

        public static void Draw()
        {
            // Let active game tick
            GameHost.Tick();
            Assets.Load();
            Theme.DrawThemePanel();

            // Sync snapshot
            var s = Snapshots.Current;
            VmmInnit = s.VmmReady;
            AttachedToProccess = s.VmmReady && s.Pid > 0 && s.MainBase != 0;

            // transitions
            if (!_prevVmmInnit && VmmInnit)
            {
                VmmService.RefreshProcesses();
                _procSelectedIndex = -1;
                _procFilter = string.Empty;
            }
            if (!_prevAttached && AttachedToProccess)
            {
                VmmService.RefreshModules();
                _modFilter = string.Empty;
            }
            _prevVmmInnit = VmmInnit;
            _prevAttached = AttachedToProccess;

            // HOME window (kept alive to allow game Draw() to run ESP even when menus hidden)
            if (_homeVisible)
            {
                if (_allMenusHidden)
                {
                    ImGui.SetNextWindowPos(new Vector2(-10000, -10000), ImGuiCond.Always);
                    ImGui.SetNextWindowSize(new Vector2(1, 1), ImGuiCond.Always);
                    ImGui.SetNextWindowCollapsed(true, ImGuiCond.Always);
                    ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));
                    ImGui.Begin("Home", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBringToFrontOnFocus);
                    // No menu UI, but still call game Draw to keep overlay/ESP alive
                    GameHost.Draw(ImGuiWindowFlags.None);
                    ImGui.End();
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.Begin("Home", ImGuiWindowFlags.None);
                    ImGui.Checkbox("Service Control Visible", ref _serviceControlVisible);
                    ImGui.Checkbox("Keybinds Visible", ref _keybindsVisible);
                    ImGui.Checkbox("Show Top Infobar", ref _showTopInfoBar);
                    DrawCombo("Game");
                    ImGui.Separator();
                    GameHost.Draw(ImGuiWindowFlags.None);
                    ImGui.Separator();
                    ImGui.End();
                }
            }

            // SERVICE CONTROL window (skip when globally hidden)
            if (_serviceControlVisible && !_allMenusHidden)
            {
                ImGui.Begin("Service · Control", ImGuiWindowFlags.NoCollapse);

                // VMM controls
                bool fVmm = BeginFold("svc.vmm", "VMM Controls", defaultOpen: true);
                if (fVmm)
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), s.Status);
                    ImGui.Separator();
                    ImGui.TextDisabled($"VMM Ready: {VmmInnit} | Attached: {AttachedToProccess} | PID: {s.Pid} | Base: 0x{s.MainBase:X}");

                    if (ImGui.Button("Init VMM (no attach)")) VmmService.InitOnly();
                    ImGui.SameLine();
                    if (ImGui.Button("Dispose VMM")) VmmService.DisposeVmm();

                    ImGui.Separator();
                    ImGui.TextDisabled($"VMM Ready: {s.VmmReady} | PID: {s.Pid} | Base: 0x{s.MainBase:X}");
                    EndFold(fVmm);
                }

                // Processes
                bool fProc = BeginFold("svc.proc", "Attach & Processes", defaultOpen: false);
                if (fProc)
                {
                    ImGui.SetNextItemWidth(260);
                    ImGui.InputTextWithHint("##proc_filter", "Search processes…", ref _procFilter, 256);

                    ImGui.SameLine();
                    if (ImGui.Button("Refresh Processes"))
                        VmmService.RefreshProcesses();

                    ImGui.Separator();
                    ImGui.Text("Processes:");

                    var procs = s.Processes ?? Array.Empty<DmaMemory.ProcEntry>();
                    var filtered = FilterProcesses(procs, _procFilter).ToArray();

                    if (_procSelectedIndex >= filtered.Length) _procSelectedIndex = -1;

                    ImGui.BeginChild("proc_child", new Vector2(0, 150), ImGuiChildFlags.None);
                    for (int i = 0; i < filtered.Length; i++)
                    {
                        var p = filtered[i];
                        bool sel = (i == _procSelectedIndex);
                        if (ImGui.Selectable($"{p.Pid,6}  {p.Name}{(p.IsWow64 ? " (Wow64)" : "")}", sel))
                            _procSelectedIndex = i;
                    }
                    ImGui.EndChild();

                    if (_procSelectedIndex >= 0 && _procSelectedIndex < filtered.Length)
                    {
                        var chosen = filtered[_procSelectedIndex];
                        ImGui.TextColored(new Vector4(0.6f, 1f, 0.6f, 1f),
                            $"Selected: {chosen.Name} (PID {chosen.Pid})");

                        if (ImGui.Button("Attach Selected"))
                            VmmService.Attach(chosen.Name); // modules auto-refresh
                    }
                    else
                    {
                        ImGui.TextDisabled("No process selected.");
                    }
                    EndFold(fProc);
                }

                // Modules
                bool fMods = BeginFold("svc.mods", "Modules", defaultOpen: false);
                if (fMods)
                {
                    ImGui.SetNextItemWidth(260);
                    ImGui.InputTextWithHint("##mod_filter", "Search modules…", ref _modFilter, 256);

                    ImGui.SameLine();
                    if (ImGui.Button("Refresh Modules")) VmmService.RefreshModules();

                    ImGui.Text("Modules:");
                    var mods = s.Modules ?? Array.Empty<DmaMemory.ModuleInfo>();
                    var mFiltered = FilterModules(mods, _modFilter).ToArray();

                    ImGui.BeginChild("mods_child", new Vector2(0, 150), ImGuiChildFlags.None);
                    foreach (var m in mFiltered)
                        ImGui.TextUnformatted($"{m.Name,-28} Base=0x{m.Base:X} Size=0x{m.Size:X}");
                    ImGui.EndChild();
                    EndFold(fMods);
                }

                // Display & Frame pacing
                bool fDisp = BeginFold("svc.display", "Display & Frame Pacing", defaultOpen: true);
                if (fDisp)
                {
                    DrawMonitorSettings();
                    EndFold(fDisp);
                }

                // Input Manager & Makcu
                DrawInputManager();

                // Small toggles
                ImGui.Separator();
                ImGui.Checkbox("Show Top Info Bar", ref _showTopInfoBar);
                ImGui.SameLine();
                ImGui.Checkbox("Lock Top Bar", ref _topBarLocked);

                ImGui.End(); // "Service · Control"
            }

            // KEYBINDS window (dedicated)
            if (_keybindsVisible && !_allMenusHidden)
                DrawKeybindsWindow();

            // Render top bar if enabled
            if (_showTopInfoBar && !_allMenusHidden) DrawTopInfoBar();

            // Execute binds (poll if needed)
            ExecuteKeybindsFrame();
        }

        // ---------------- Keybinds Window ----------------

        private static void DrawKeybindsWindow()
        {
            ImGui.Begin("Keybinds", ImGuiWindowFlags.NoCollapse);

            // discover profiles on first draw / if empty
            if (_profiles.Count == 0)
            {
                _profiles = KeybindRegistry.Discover();
                if (_profiles.Count > 0)
                {
                    _selectedProfileIndex = 0;
                    LoadActiveProfile(_profiles[0]);
                }
            }

            // profile picker
            using (UiLayout.PushFieldWidth(420))
            {
                string prev = _profiles.Count > 0
                    ? _profiles[_selectedProfileIndex].Display
                    : "(no profiles)";

                if (ImGui.BeginCombo("Profile", prev))
                {
                    for (int i = 0; i < _profiles.Count; i++)
                    {
                        bool sel = (i == _selectedProfileIndex);
                        if (ImGui.Selectable(_profiles[i].Display, sel))
                        {
                            _selectedProfileIndex = i;
                            LoadActiveProfile(_profiles[i]);
                        }
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Rescan"))
            {
                _profiles = KeybindRegistry.Discover();
                if (_profiles.Count > 0)
                {
                    _selectedProfileIndex = Math.Min(_selectedProfileIndex, _profiles.Count - 1);
                    LoadActiveProfile(_profiles[_selectedProfileIndex]);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Save"))
            {
                if (!string.IsNullOrEmpty(_activeProfilePath))
                    KeybindRegistry.Save(_activeProfilePath, _activeProfile);
            }

            ImGui.Separator();
            ImGui.TextDisabled("Binds:");

            // --- compute column widths from content (auto-fit, no wasted space) ---
            var style = ImGui.GetStyle();
            float cellPadX   = style.CellPadding.X;
            float framePadX  = style.FramePadding.X;
            float itemSpaceX = style.ItemSpacing.X;

            float TextW(string s) => ImGui.CalcTextSize(s ?? "").X;
            float BtnW(string s)  => TextW(s) + framePadX * 2f;
            string ModsStr(KeybindEntry b) =>
                $"{(b.Ctrl ? "Ctrl " : "")}{(b.Shift ? "Shift " : "")}{(b.Alt ? "Alt" : "")}".Trim();

            float nameW   = TextW("Name");
            float keyW    = TextW("Key");
            float modsW   = TextW("Mods");
            float modeW   = TextW("Mode");
            float actionW = TextW("Action");
            float editW   = TextW("Edit");

            foreach (var b in _activeProfile.Binds)
            {
                nameW   = MathF.Max(nameW,   TextW(b.Name));
                keyW    = MathF.Max(keyW,    TextW(VkNames.Name(b.Vk) ?? $"VK {b.Vk:X2}"));
                modsW   = MathF.Max(modsW,   TextW(ModsStr(b)));
                modeW   = MathF.Max(modeW,   TextW(b.Mode.ToString()));
                actionW = MathF.Max(actionW, TextW(b.Action));
            }

            float editBtnW = BtnW("Edit") + itemSpaceX + BtnW("Delete");
            editW = MathF.Max(editW, editBtnW);

            float pad(float w) => w + cellPadX * 2f + 4f;
            keyW    = pad(keyW);
            modsW   = pad(modsW);
            modeW   = pad(modeW);
            actionW = pad(actionW);
            editW   = pad(editW);

            var tflags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                       | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollX;

            if (ImGui.BeginTable("binds", 6, tflags))
            {
                ImGui.TableSetupColumn("Name",   ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("Key",    ImGuiTableColumnFlags.WidthFixed,   keyW);
                ImGui.TableSetupColumn("Mods",   ImGuiTableColumnFlags.WidthFixed,   modsW);
                ImGui.TableSetupColumn("Mode",   ImGuiTableColumnFlags.WidthFixed,   modeW);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed,   actionW);
                ImGui.TableSetupColumn("Edit",   ImGuiTableColumnFlags.WidthFixed,   editW);
                ImGui.TableHeadersRow();

                for (int i = 0; i < _activeProfile.Binds.Count; i++)
                {
                    var b = _activeProfile.Binds[i];
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(b.Name);
                    ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(VkNames.Name(b.Vk) ?? $"VK {b.Vk:X2}");
                    ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(ModsStr(b));
                    ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(b.Mode.ToString());
                    ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted(b.Action);

                    ImGui.TableSetColumnIndex(5);
                    if (ImGui.SmallButton($"Edit##{i}")) BeginEditBind(i);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Delete##{i}"))
                    {
                        _activeProfile.Binds.RemoveAt(i);
                        PersistAndReregister();
                        break;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button("Add Keybind"))
            {
                BeginEditBind(-1);
            }

            // Editor modal
            DrawBindEditorModal();

            ImGui.End(); // "Keybinds"
        }

        private static void BeginEditBind(int index)
        {
            _editingIndex = index;
            if (index >= 0 && index < _activeProfile.Binds.Count)
            {
                var b = _activeProfile.Binds[index];
                _newBindName = b.Name;
                _rebindVk = b.Vk;
                _rebindCtrl = b.Ctrl;
                _rebindShift = b.Shift;
                _rebindAlt = b.Alt;
                _editActionName = b.Action;
                _newBindMode = b.Mode;
            }
            else
            {
                _newBindName = "New Bind";
                _rebindVk = 0;
                _rebindCtrl = _rebindShift = _rebindAlt = false;
                _editActionName = nameof(KeybindAction.ToggleServiceControl);
                _newBindMode = KeybindMode.OnPress;
            }
            _wantsRebind = false;
            ImGui.OpenPopup("Edit Keybind");
        }

        private static void DrawBindEditorModal()
        {
            bool open = true;
            if (ImGui.BeginPopupModal("Edit Keybind", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.InputText("Name", ref _newBindName, 128);

                if (!_wantsRebind)
                {
                    if (ImGui.Button("Click to bind"))
                    {
                        _wantsRebind = true;
                        _rebindVk = 0;
                    }
                }
                else
                {
                    ImGui.TextDisabled("Press any key or mouse… (Esc to cancel)");
                    CaptureAnyKey();
                }

                ImGui.SameLine();
                ImGui.Text($"Current: {( _rebindVk==0 ? "<none>" : VkNames.Name(_rebindVk) ?? $"VK {_rebindVk:X2}")}");

                ImGui.Separator();

                ImGui.Checkbox("Ctrl", ref _rebindCtrl); ImGui.SameLine();
                ImGui.Checkbox("Shift", ref _rebindShift); ImGui.SameLine();
                ImGui.Checkbox("Alt", ref _rebindAlt);

                if (ImGui.BeginCombo("Mode", _newBindMode.ToString()))
                {
                    foreach (KeybindMode m in Enum.GetValues(typeof(KeybindMode)))
                    {
                        bool sel = m == _newBindMode;
                        if (ImGui.Selectable(m.ToString(), sel)) _newBindMode = m;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                // Build the composite action list: Global + Category-specific
                var actions = new List<string>(_globalActions);
                actions.AddRange(MamboDMA.Input.Keybinds.GetActions(_activeProfile.Category));

                int curIdx = Math.Max(0, actions.FindIndex(a => string.Equals(a, _editActionName, StringComparison.Ordinal)));
                string preview = (curIdx >= 0 && curIdx < actions.Count) ? actions[curIdx] : actions[0];

                if (ImGui.BeginCombo("Action", preview))
                {
                    for (int i = 0; i < actions.Count; i++)
                    {
                        bool sel = (i == curIdx);
                        if (ImGui.Selectable(actions[i], sel))
                        {
                            _editActionName = actions[i];
                            curIdx = i;
                        }
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                ImGui.Separator();
                if (ImGui.Button("Save"))
                {
                    if (_rebindVk != 0)
                    {
                        var entry = new KeybindEntry
                        {
                            Name  = _newBindName,
                            Vk    = _rebindVk,
                            Ctrl  = _rebindCtrl,
                            Shift = _rebindShift,
                            Alt   = _rebindAlt,
                            Mode  = _newBindMode,
                            Action = _editActionName      // store string
                        };

                        if (_editingIndex >= 0 && _editingIndex < _activeProfile.Binds.Count)
                            _activeProfile.Binds[_editingIndex] = entry;
                        else
                            _activeProfile.Binds.Add(entry);

                        PersistAndReregister();
                        ImGui.CloseCurrentPopup();
                    }
                    else
                    {
                        ImGui.OpenPopup("PickKeyWarn");
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.BeginPopup("PickKeyWarn"))
                {
                    ImGui.Text("Please press a key or mouse button.");
                    if (ImGui.Button("OK")) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }

                ImGui.EndPopup();
            }
        }

        private static void CaptureAnyKey()
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _wantsRebind = false;
                return;
            }

            bool ctrl = MamboDMA.Input.InputManager.IsKeyDown(VK.CONTROL);
            bool shift = MamboDMA.Input.InputManager.IsKeyDown(VK.SHIFT);
            bool alt   = MamboDMA.Input.InputManager.IsKeyDown(VK.MENU);

            for (int vk = 1; vk < 255; vk++)
            {
                if (MamboDMA.Input.InputManager.IsKeyPressed(vk))
                {
                    _rebindVk = vk;
                    _rebindCtrl = ctrl;
                    _rebindShift = shift;
                    _rebindAlt = alt;
                    _wantsRebind = false;
                    break;
                }
            }
        }

        private static void PersistAndReregister()
        {
            if (!string.IsNullOrEmpty(_activeProfilePath))
                KeybindRegistry.Save(_activeProfilePath, _activeProfile);

            _hotkeysRegistered = false; // re-register next frame
        }

        private static void LoadActiveProfile(KeybindRegistry.ProfileRef pref)
        {
            _activeProfilePath = pref.Path;
            _activeProfile = KeybindRegistry.Load(pref.Path);
            _hotkeysRegistered = false;
        }

        // Execute binds each frame; use InputManager registrations when ready, else poll
        private static void ExecuteKeybindsFrame()
        {
            if (MamboDMA.Input.InputManager.IsReady)
            {
                if (!_hotkeysRegistered)
                {
                    MamboDMA.Input.InputManager.ClearAllHotkeys();
                    foreach (var b in _activeProfile.Binds)
                    {
                        var mode = b.Mode switch
                        {
                            KeybindMode.OnPress    => MamboDMA.Input.InputManager.HotkeyMode.OnPress,
                            KeybindMode.OnRelease  => MamboDMA.Input.InputManager.HotkeyMode.OnRelease,
                            KeybindMode.WhileDown  => MamboDMA.Input.InputManager.HotkeyMode.WhileDown,
                            _ => MamboDMA.Input.InputManager.HotkeyMode.OnPress
                        };

                        MamboDMA.Input.InputManager.RegisterHotkey(b.Vk, mode, () =>
                        {
                            bool ctrl = MamboDMA.Input.InputManager.IsKeyDown(VK.CONTROL);
                            bool shift = MamboDMA.Input.InputManager.IsKeyDown(VK.SHIFT);
                            bool alt = MamboDMA.Input.InputManager.IsKeyDown(VK.MENU);
                            if (!b.MatchesModifiers(ctrl, shift, alt)) return;
                            RunActionString(b.Action);
                        });
                    }
                    _hotkeysRegistered = true;
                }
            }
            else
            {
                foreach (var b in _activeProfile.Binds)
                {
                    bool press = MamboDMA.Input.InputManager.IsKeyPressed(b.Vk);
                    bool down  = MamboDMA.Input.InputManager.IsKeyDown(b.Vk);
                    bool fire  =
                        (b.Mode == KeybindMode.OnPress   && press) ||
                        (b.Mode == KeybindMode.WhileDown && down);
                    if (!fire) continue;

                    if (b.Ctrl || b.Shift || b.Alt) continue; // can't verify mods here
                    RunActionString(b.Action);
                }
            }
        }

        // New: string-based dispatcher (global enum first, else game/category)
        private static void RunActionString(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return;

            if (Enum.TryParse<KeybindAction>(actionName, out var ga))
            {
                RunAction(ga);
                return;
            }

            // Game/category scoped:
            if (!Keybinds.Dispatch(_activeProfile.Category, actionName))
            {
                // Unknown action; ignore (or log if you want)
                // Console.WriteLine($"[Keybind] Unknown action: {actionName} in category '{_activeProfile.Category}'");
            }
        }

        private static void RunAction(KeybindAction a)
        {
            switch (a)
            {
                case KeybindAction.ToggleServiceControl: _serviceControlVisible = !_serviceControlVisible; break;
                case KeybindAction.ToggleHomeWindow:     _homeVisible = !_homeVisible; break;
                case KeybindAction.ToggleTopBar:         _showTopInfoBar = !_showTopInfoBar; break;
                case KeybindAction.ToggleKeybindsWindow: _keybindsVisible = !_keybindsVisible; break;

                case KeybindAction.InitVmm:              VmmService.InitOnly(); break;
                case KeybindAction.DisposeVmm:           VmmService.DisposeVmm(); break;
                case KeybindAction.RefreshProcesses:     VmmService.RefreshProcesses(); break;
                case KeybindAction.AttachSelectedProcess: TryAttachCurrentlySelectedProcess(); break;

                case KeybindAction.CenterWindow:         Misc.CenterOnMonitor(_selectedMonitor); break;
                case KeybindAction.RestoreDecorations:   Misc.RestoreDecorations(); break;

                case KeybindAction.ToggleAllMenus:
                    _allMenusHidden = !_allMenusHidden;
                    UiVisibility.MenusHidden = _allMenusHidden; // NEW: tell everyone
                    break;
            }
        }

        private static void TryAttachCurrentlySelectedProcess()
        {
            var s = Snapshots.Current;
            var procs = s.Processes ?? Array.Empty<DmaMemory.ProcEntry>();
            var filtered = FilterProcesses(procs, _procFilter).ToArray();
            if (_procSelectedIndex >= 0 && _procSelectedIndex < filtered.Length)
                VmmService.Attach(filtered[_procSelectedIndex].Name);
        }

        // ---------------- Existing UI helpers ----------------

        private static IEnumerable<DmaMemory.ProcEntry> FilterProcesses(IEnumerable<DmaMemory.ProcEntry> list, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return list;
            filter = filter.Trim();
            bool allDigits = filter.All(char.IsDigit);
            return list.Where(p =>
                (!allDigits && p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (allDigits && p.Pid.ToString().Contains(filter, StringComparison.Ordinal)));
        }

        private static IEnumerable<DmaMemory.ModuleInfo> FilterModules(IEnumerable<DmaMemory.ModuleInfo> list, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return list;
            filter = filter.Trim();
            return list.Where(m =>
                m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                $"0x{m.Base:X}".Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        private static void LoadLastGameOnce(string[] names)
        {
            if (_loadedLastGame) return;
            _loadedLastGame = true;
            try
            {
                if (File.Exists(_lastGamePath))
                {
                    var wanted = (File.ReadAllText(_lastGamePath) ?? "").Trim();
                    if (!string.IsNullOrEmpty(wanted) &&
                        names.Any(n => string.Equals(n, wanted, StringComparison.Ordinal)))
                    {
                        GameRegistry.Select(wanted);
                    }
                }
            }
            catch { /* ignore */ }
        }

        private static void SaveLastGame(string name)
        {
            try { File.WriteAllText(_lastGamePath, name ?? ""); } catch { /* ignore */ }
        }

        public static void DrawCombo(string label = "Game")
        {
            var names = GameRegistry.Names?.ToArray() ?? Array.Empty<string>();
            if (names.Length == 0)
            {
                ImGui.TextDisabled("No games registered");
                return;
            }

            if (GameRegistry.Active is null)
                LoadLastGameOnce(names);

            if (GameRegistry.Active is null)
                GameRegistry.Select(names[0]);

            var activeName = GameRegistry.Active?.Name ?? names[0];
            int cur = Array.IndexOf(names, activeName);
            if (cur < 0) cur = 0;

            ImGui.SetNextItemWidth(250);
            if (ImGui.BeginCombo(label, names[cur]))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    bool selected = (i == cur);
                    if (ImGui.Selectable(names[i], selected) && i != cur)
                    {
                        GameRegistry.Select(names[i]);
                        SaveLastGame(names[i]);
                    }
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
            if (b == 0)
            {
                Misc.ApplyMonitorSelection(_selectedMonitor, _applyBorderless, _applyFullscreen);
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
                _logoTex = SvgLoader.LoadSvg("Assets/Img/Logo.svg", 32); // 32px tall
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

            var flags = ImGuiWindowFlags.NoTitleBar
                        | ImGuiWindowFlags.NoScrollbar
                        | ImGuiWindowFlags.NoCollapse
                        | ImGuiWindowFlags.NoDocking
                        | ImGuiWindowFlags.AlwaysAutoResize;

            if (!_topBarPrimed)
            {
                float posX = vp.WorkPos.X + vp.WorkSize.X * 0.5f;
                float posY = vp.WorkPos.Y + 8f;
                ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0f));
                _topBarPrimed = true;
            }

            ImGui.SetNextWindowSizeConstraints(new Vector2(0, chipH), new Vector2(9999, chipH));

            if (ImGui.Begin("TopInfoBar", flags))
            {
                var winPos = ImGui.GetWindowPos();
                float gripW = 18f;
                ImGui.InvisibleButton("##draggrip", new Vector2(gripW, chipH - 6f));
                bool canDrag = !_topBarLocked && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left);
                if (canDrag)
                {
                    var delta = ImGui.GetIO().MouseDelta;
                    ImGui.SetWindowPos(winPos + delta);
                    winPos = ImGui.GetWindowPos();
                }

                var dl = ImGui.GetWindowDrawList();
                float gy = winPos.Y + (chipH * 0.5f);
                uint gripCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.65f, 0.70f, 0.82f, 0.85f));
                for (int i = 0; i < 3; i++)
                {
                    float gx = winPos.X + 6f + i * 4.5f;
                    dl.AddCircleFilled(new Vector2(gx, gy), 1.4f, gripCol);
                }

                ImGui.SameLine(0, 6);
                EnsureLogo();
                if (_logoTex.Id != 0)
                {
                    float targetH = chipH - 8f;
                    float aspect = (float)_logoTex.Width / _logoTex.Height;
                    float logoW = targetH * aspect;
                    float logoYOffset = (chipH - targetH) * 0.5f;
                    ImGui.SetCursorPosY(logoYOffset);
                    ImGui.Image((IntPtr)_logoTex.Id, new Vector2(logoW, targetH));
                    ImGui.SameLine(0, 6);
                }

                ImGui.PushFont(Fonts.Bold);
                var textSize = ImGui.CalcTextSize("AMBO");
                float textYOffset = (chipH - textSize.Y) * 0.5f;
                ImGui.SetCursorPosY(textYOffset);
                ImGui.TextUnformatted("AMBO");
                ImGui.PopFont();

                ImGui.SameLine(0, 10);
                ImGui.SetCursorPosY(textYOffset);
                ImGui.TextDisabled($"· {ImGui.GetIO().Framerate:0} FPS");

                var scr = ScreenService.Current;
                int hz = Raylib.GetMonitorRefreshRate(SelectedMonitor);
                ImGui.SameLine(0, 10);
                ImGui.SetCursorPosY(textYOffset);
                ImGui.TextDisabled($"· {scr.W}×{scr.H} @{hz}Hz");

                ImGui.SameLine();
                ImGui.Dummy(new Vector2(8, 1));
                ImGui.SameLine();

                ImGui.SetCursorPosY(textYOffset);
                if (ImGui.SmallButton(_topBarLocked ? "Unlock" : "Lock"))
                    _topBarLocked = !_topBarLocked;

                ImGui.SameLine(0, 10);
                float btnH = chipH - 14f;
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.80f, 0.20f, 0.20f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.00f, 0.30f, 0.30f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.70f, 0.15f, 0.15f, 1f));
                if (ImGui.Button("X", new Vector2(28f, btnH)))
                    OverlayWindowApi.Quit();
                ImGui.PopStyleColor(3);

                if (ImGui.BeginPopupContextWindow("TopBarCtx", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
                {
                    if (ImGui.MenuItem(_topBarLocked ? "Unlock position" : "Lock position"))
                        _topBarLocked = !_topBarLocked;

                    if (ImGui.MenuItem("Hide Top Bar"))
                        _showTopInfoBar = false;

                    ImGui.EndPopup();
                }
            }

            ImGui.End();
            ImGui.PopStyleVar(3);
        }

        public static void DrawInputManager()
        {
            bool fInput = BeginFold("svc.input", "Input Manager & Makcu", defaultOpen: false);
            if (!fInput) return;

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

            if (!VmmInnit) ImGui.BeginDisabled();
            if (ImGui.Button("Init InputManager"))
            {
                JobSystem.Schedule(() =>
                {
                    var adapter = new DmaMemory.VmmSharpExAdapter(DmaMemory.Vmm!);
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
            if (!VmmInnit)
            {
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Init blocked: VMM not initialized.");
                ImGui.EndDisabled();
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

        private static void DrawStatusLight(string label, bool state)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
            Vector4 col = state
                ? new Vector4(0, 0.8f, 0, 1)
                : new Vector4(0.9f, 0.3f, 0.1f, 1);
            dl.AddCircleFilled(new Vector2(p.X + 6, y), 5, ImGui.ColorConvertFloat4ToU32(col));
            ImGui.Dummy(new Vector2(16, ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.TextDisabled(label);
        }
    }
}
