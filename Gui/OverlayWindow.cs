using System;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using static MamboDMA.StyleEditorUI;

namespace MamboDMA;

public sealed class OverlayWindow : IDisposable
{
    private bool _running = true;

    public OverlayWindow(string title = "MamboDMA", int width = 1100, int height = 700)
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.UndecoratedWindow);
        Raylib.InitWindow(width, height, title);
        Raylib.SetTargetFPS(144);

        // ImGui context + renderer hookup
        rlImGui.Setup();

        // Load config (if present) then apply current settings
        StyleConfig.Load(); // AppData/Roaming/MamboDMA/Examples/config.json
        StyleConfig.ApplyToImGui(StyleConfig.Settings);

        // Fonts & base style
        SetupImGuiStyleAndFonts();
    }

    public void Run(Action drawUI)
    {
        while (_running && !Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            rlImGui.Begin();

            DockspaceOverMainViewport();
            drawUI();

            rlImGui.End();
            Raylib.EndDrawing();
        }
    }

    public void Close() => _running = false;

    public void Dispose()
    {
        try { rlImGui.Shutdown(); } catch { }
        try { if (!Raylib.WindowShouldClose()) Raylib.CloseWindow(); } catch { }
    }

    private static unsafe void SetupImGuiStyleAndFonts()
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        ImGui.StyleColorsDark();

        var style = ImGui.GetStyle();
        style.WindowRounding = 8f;
        style.FrameRounding = 8f;
        style.GrabRounding = 8f;

        const float baseSize = 16f;

        // Create native font config
        ImFontConfigPtr cfg = ImGuiNative.ImFontConfig_ImFontConfig();
        cfg.OversampleH = 3;
        cfg.OversampleV = 2;
        cfg.PixelSnapH = false;
        cfg.MergeMode = false;
        cfg.GlyphOffset = Vector2.Zero;

        // Load weights from your Assets/Fonts/static/
        Fonts.Regular = io.Fonts.AddFontFromFileTTF("Assets/Fonts/AlanSans-Regular.ttf", baseSize, cfg);
        Fonts.Medium = io.Fonts.AddFontFromFileTTF("Assets/Fonts/AlanSans-Medium.ttf", baseSize, cfg);
        Fonts.Bold = io.Fonts.AddFontFromFileTTF("Assets/Fonts/AlanSans-Bold.ttf", baseSize, cfg);

        cfg.Destroy();

        rlImGui.ReloadFonts();

        unsafe { io.NativePtr->FontDefault = Fonts.Regular.NativePtr; }
    }

    private static void DockspaceOverMainViewport()
    {
        var vp = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(vp.Pos);
        ImGui.SetNextWindowSize(vp.Size);
        ImGui.SetNextWindowViewport(vp.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));

        var flags =
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus;

        ImGui.Begin("##DockRoot", flags);
        ImGui.PopStyleVar(3);

        var dockspaceId = ImGui.GetID("MamboDockspace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

        ImGui.End();
    }
    internal static class Fonts
    {
        public static ImFontPtr Regular;
        public static ImFontPtr Medium;
        public static ImFontPtr Bold;
    }     
}
