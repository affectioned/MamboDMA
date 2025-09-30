# MamboDMA — Multi-Game DMA Overlay & Tools (C# / .NET)

This project provides a **pluggable DMA overlay framework** built with:

- **DMA/VMM backend** via [VmmSharpEx](https://github.com/LonePointer/VmmSharpEx)  
- **Raylib-cs** + **ImGui.NET** (via rlImGui) for rendering  
- **JobSystem** for threaded background work  
- **Config & Snapshot system** for per-game persistence and fast UI updates  
- **Game plugin architecture** (`IGame`, `GameRegistry`, `GameSelector`)  
- **SVG/PNG assets loader** (logos, icons)  

Everything runs out of the box, the UI is dockable, and the service layer is designed for adding new games and overlays.

---

## Contents
- [Features](#features)  
- [Screens](#screens)  
- [Project layout](#project-layout)  
- [Requirements](#requirements)  
- [Build & run](#build--run)  
- [Game plugins](#game-plugins)  
- [JobSystem](#jobsystem)  
- [Assets (PNG & SVG)](#assets-png--svg)  
- [Config & style](#config--style)  
- [Top Info Bar](#top-info-bar)  
- [Troubleshooting](#troubleshooting)  

---

## Features
- Register any number of game plugins (DayZ, Reforger, Example, etc.)
- Switch active game via dropdown (`GameSelector`)
- Per-game workers (attach, entity updates, ESP drawing)
- Service panels for VMM control, process attach, modules, display settings
- Input manager & Makcu device support
- SVG/PNG logo support with crisp scaling
- Config persistence in `%AppData%\MamboDMA`

---

## Screens
- **Home**: Select game, attach VMM, processes, modules  
- **Game Panels**: Per-game UI (ESP toggles, debug windows, workers)  
- **Top Info Bar**: Logo + title + FPS + resolution + close button  
- **Service Control**: VMM init/dispose, attach, input manager  

---

## Project layout
```
MamboDMA/
  Program.cs              // entry point (UI mode selection + GameRegistry setup)
  OverlayWindow.cs        // Raylib + rlImGui init, ImGui fonts, dockspace
  Games/
    IGame.cs              // plugin contract
    GameRegistry.cs       // register/select/stop active game
    GameSelector.cs       // UI dropdown, top bar, service panels
    GameHost.cs           // thin glue to tick/draw the active game
    DayZ/DayZGame.cs      // full example plugin with updater & ESP
    Reforger/ReforgerGame.cs
    Example/ExampleGame.cs
  Services/
    JobSystem.cs          // thread pool + cancellation
    VmmService.cs         // VMM lifecycle + snapshots
    Snapshots.cs          // app-wide snapshot state
  Gui/
    Assets.cs             // load PNGs to Texture2D
    SvgLoader.cs          // rasterize SVG → Texture2D (SkiaSharp)
  Misc.cs                 // helpers (monitor selection, etc.)
  StyleConfig.cs          // ImGui theme persistence
```

---

## Requirements
- Windows 10/11  
- .NET 9 SDK  
- `VmmSharpEx` runtime (DMA device attached, e.g., `fpga`)  
- GPU with raylib/DX11  
- Fonts: `Assets/Fonts/AlanSans-*.ttf`  

---

## Build & run
```bash
dotnet build -c Release
dotnet run -c Release
```

On launch, you choose the UI mode:
```
Choose UI:
  [1] OverlayUI (Advanced)
  [2] ServiceDemoUI (Simple)
  [3] Game UI (with game selector)
```

Pick **3** for multi-game mode.

---

## Game plugins

Each game implements `IGame`:

```csharp
public sealed class MyGame : IGame {
    public string Name => "MyGame";
    public void Initialize() { /* one-time setup */ }
    public void Start() { /* spin up JobSystem workers */ }
    public void Stop() { /* stop workers */ }
    public void Tick() { /* lightweight per-frame logic */ }
    public void Draw(ImGuiWindowFlags flags) {
        // ImGui controls, ESP overlays, debug windows
    }
}
```

Register in `Program.cs`:
```csharp
GameRegistry.Register(new DayZGame());
GameRegistry.Register(new ReforgerGame());
GameRegistry.Register(new MyGame());
```

Select from UI → Game combo.

---

## JobSystem
- **Start**: `JobSystem.Start(workers: 3);`  
- **Schedule**: run actions or loops in background threads  
- **Stop**: cancels all tokens and shuts down threads  

Example periodic worker:
```csharp
JobSystem.Schedule(async ct => {
    while (!ct.IsCancellationRequested) {
        DmaMemory.ScatterRound(rd => { /* bulk reads */ });
        Snapshots.Mutate(s => s with { Status = "Updated" });
        await Task.Delay(50, ct);
    }
});
```

---

## Assets (PNG & SVG)

- **PNG**:  
  ```csharp
  var img = Raylib.LoadImage("Assets/Img/Logo.png");
  var tex = Raylib.LoadTextureFromImage(img);
  Raylib.UnloadImage(img);
  ```

- **SVG** (via Skia):  
  ```csharp
  Texture2D tex = SvgLoader.LoadSvg("Assets/Img/Logo.svg", 32);
  ImGui.Image((IntPtr)tex.Id, new Vector2(tex.Width, tex.Height));
  ```

Assets are auto-loaded via `Assets.Load()`.

---

## Config & style
- Configs are JSON in `%AppData%\MamboDMA\<GameName>\config.json`  
- Use `Config<T>.DrawConfigPanel(...)` for UI-bound config  
- `StyleConfig` saves ImGui style + fonts  

---

## Top Info Bar
A slim status bar with:
- Logo (SVG/PNG)  
- Project title text  
- FPS counter  
- Current resolution & refresh rate  
- Close button  

Position: centered at top of viewport. Auto-resizes width to content.

---

## Troubleshooting
- **OBS/Discord capture**: run with same elevation; disable “fullscreen optimizations”.  
- **Window icon not updating**: ensure `.ico` contains all sizes (16, 24, 32, 48, 64, 128, 256).  
- **Fonts missing**: check `Assets/Fonts` copy settings.  

---

## License / attribution
- UI: ImGui.NET + raylib-cs + rlImGui  
- Backend: VmmSharpEx  
- SVG: Svg.Skia + SkiaSharp  

> Thanks to Lone for VmmSharpEx
