# MamboDMA — Example DMA Overlay & Tools (C# / .NET)

A compact example project that shows how to:

- Initialize and talk to a DMA/VMM backend (`VmmSharpEx`)  
- Attach to a target process and enumerate processes/modules  
- Read memory directly, follow pointer chains, and do **scatter reads**  
- Drive a responsive UI with **ImGui.NET** + **raylib-cs**  
- Run background work safely via a tiny **JobSystem** (thread pool + channel)  
- Keep UI snappy by publishing results into a shared **snapshot** state

> This repo is intentionally small but practical: everything runs, the UI is usable, and the services are structured for you to add your own game/app logic.

---

## Contents

- [Screens](#screens)
- [Project layout](#project-layout)
- [Requirements](#requirements)
- [Build & run](#build--run)
- [How it works (high level)](#how-it-works-high-level)
- [JobSystem (threads & background work)](#jobsystem-threads--background-work)
- [VMM service (attach, list, dispose)](#vmm-service-attach-list-dispose)
- [Memory helpers (read, pointer chains, scatter)](#memory-helpers-read-pointer-chains-scatter)
- [UI walkthrough](#ui-walkthrough)
- [Style, config & fonts](#style-config--fonts)
- [Common patterns / recipes](#common-patterns--recipes)
- [Troubleshooting capture/streaming](#troubleshooting-capturestreaming)
- [Notes](#notes)

---

## Screens

- **Home**: VMM init/attach, process list, display controls (monitor, borderless/fullscreen)
- **Memory**: modules list, offsets browser, chain builder (Base → +Offsets → Deref → ReturnAs)
- **Settings**: ImGui style editor + save/load configs
- **About**: credits

---

## Project layout

```
MamboDMA/
  DmaMemory.cs        // core DMA helpers (attach, read, scatter, signatures, symbols)
  Services/
    JobSystem.cs      // tiny work scheduler (channels + Tasks)
    VmmService.cs     // async wrappers that use JobSystem + publish to Snapshots
  OverlayWindow.cs    // raylib window + ImGui init, fonts, dockspace
  OverlayUI.cs        // main UI (Home, Memory, About), calls Services and DmaMemory
  StyleConfig.cs      // style/theme model + persistence + editor panel
  Program.cs          // entry point; starts JobSystem and runs UI loop
```

> The UI reads state from a `Snapshots` holder (not shown here) that the services update via `Snapshots.Mutate(...)`. Treat it as the “single source of truth” for UI-facing data.

---

## Requirements

- Windows 10/11
- .NET 8 SDK
- `VmmSharpEx` runtime (device accessible, e.g., `"fpga"` in this example)
- GPU drivers supporting raylib (DX11)  
- Fonts in `Assets/Fonts/AlanSans-*.ttf` (or change to yours)

---

## Build & run

```bash
dotnet build -c Release
dotnet run -c Release
```

On first launch, you’ll be prompted:

```
Choose UI:
  [1] OverlayUI (Advanced Example)
  [2] ServiceDemoUI (Simple Example)
Enter 1 or 2 (default = 1):
```

Pick **1** for the full example UI.

---

## How it works (high level)

- **DmaMemory** wraps `VmmSharpEx`:
  - `InitOnly` → stand up the VMM and optionally apply a memory map.
  - `TryAttachOnce` / `Attach` → resolve PID & main module base.
  - `Read<T>`, `ReadBytes`, `ReadAsciiZ`/`ReadUtf16Z` → convenient readers.
  - `TryFollow` → pointer chains (base + offsets deref).
  - `Scatter()` / `ScatterRound(...)` → multi-address reads in one round-trip.
  - Signatures and symbols helpers for advanced workflows.

- **JobSystem**: a small channel-driven work queue with N worker tasks.  
  UI enqueues jobs (attach, read lists, refresh modules), results are published into **Snapshots**, and the **UI thread** remains smooth.

- **VmmService**: thin async façade over `DmaMemory` using `JobSystem`, responsible for:
  - `InitOnly`, `Attach`, `RefreshProcesses`, `RefreshModules`, `DisposeVmm`
  - Publishing status & results: `Snapshots.Mutate(s => s with { ... })`

- **OverlayWindow**: starts raylib, ImGui, fonts, and a dockspace root.  
- **OverlayUI**: builds the windows/panels and calls into services/helpers.

---

## JobSystem (threads & background work)

`JobSystem` is a minimal worker pool:

- `Start(workers: 3)` spins up 3 background Tasks.
- `Schedule(...)` enqueues a job (supports `Action`, `Func<Task>`, and token-aware variants).
- `Stop()` cancels workers and waits briefly for a clean exit.

**API (excerpt):**
```csharp
JobSystem.Start(workers: 3);

JobSystem.Schedule(() => { /* fire & forget */ });

JobSystem.Schedule(async ct => {
    // cooperative loop
    while (!ct.IsCancellationRequested) {
        // do work
        await Task.Delay(16, ct);
    }
});

JobSystem.Stop();
```

**Best practices**
- Use the **token-aware** overload (`Action<CancellationToken>` or `Func<CancellationToken, Task>`) for loops or periodic jobs.
- Never block the UI thread for VMM/I/O—always `Schedule` it.
- Publish results to the UI via `Snapshots.Mutate(...)` to avoid cross-thread UI access.

**Example: periodic scatter poller**
```csharp
JobSystem.Schedule(async ct =>
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            // Read many values at once
            DmaMemory.ScatterRound(rd => {
                // rd.Add(out <field>, address);
                // rd.AddBuffer(buffer, address, length);
            });

            // Push the new data to UI state
            Snapshots.Mutate(s => s with { Status = "Scatter ok @ " + DateTime.Now.ToLongTimeString() });
        }
        catch (Exception ex)
        {
            Snapshots.Mutate(s => s with { Status = "Scatter error: " + ex.Message });
        }

        await Task.Delay(20, ct); // ~50 Hz
    }
});
```

> The worker will exit cleanly when `JobSystem.Stop()` cancels its token.

---

## VMM service (attach, list, dispose)

`VmmService` is the UI-friendly way to drive `DmaMemory`.

- **Init VMM (no attach)**
```csharp
VmmService.InitOnly(); // async
```

- **Attach to a process by name**
```csharp
VmmService.Attach("explorer.exe"); // async; updates Snapshots on success/failure
```

- **Refresh lists**
```csharp
VmmService.RefreshProcesses(); // fills Snapshots.Processes
VmmService.RefreshModules();   // fills Snapshots.Modules (attached PID)
```

- **Dispose**
```csharp
VmmService.DisposeVmm(); // tears down VMM and clears related snapshot state
```

The UI uses these to keep the interface fast and responsive. Each call schedules work on the `JobSystem` and updates `Snapshots`:

```csharp
Snapshots.Mutate(s => s with {
  Status   = "Attached PID=... base=0x...",
  VmmReady = true,
  Pid      = (int)DmaMemory.Pid,
  MainBase = DmaMemory.Base
});
```

---

## Memory helpers (read, pointer chains, scatter)

**Direct reads**
```csharp
// Read a value type
if (DmaMemory.Read(va, out int health)) { /* use health */ }

// Read raw bytes (best-effort)
var buf = DmaMemory.ReadBytes(va, 64);
var name = DmaMemory.ReadAsciiZ(namePtr, 64);
var u16  = DmaMemory.ReadUtf16Z(unicodePtr, 64);
```

**Pointer chains**
```csharp
// result = (((base + o0)-> + o1)-> + o2)-> ...
ulong baseVA = DmaMemory.Base;
ulong[] offs = { 0x10, 0x28, 0x18 };

if (DmaMemory.TryFollow(baseVA, offs, out var finalPtr)) {
    // finalPtr is the last deref result
}
```

**Scatter (one round, many reads)**
```csharp
DmaMemory.ScatterRound(rd =>
{
    rd.Add(out ulong localPlayer, baseVA + 0x12345678);
    rd.Add(out int health, finalPtr + 0x2C);
    rd.AddBuffer(nameBuf, namePtr, 64);
});
```

> For high-frequency loops, prefer `ScatterRound` or `Scatter()` + multiple rounds to reduce driver round-trips.

---

## UI walkthrough

- **Home**
  - **DMA Controls**: init VMM, attach to a process, dispose.
  - **Processes**: filter + list; attach to selected.
  - **Display**: choose monitor, borderless/fullscreen, center/restore; frame pacing (VSync or FPS cap).

- **Memory**
  - **Process Info**: shows current PID, main base, and “active base”.
  - **Modules**: list/filter modules; set “active base” to a selected module.
  - **Offsets**: loads `offsets.json` (names panel).
  - **Read Chain Builder**:
    - Build: **Start Base** → **+Offset / +Const / +(i±1)*stride** → **Deref** → **ReturnAs** (Ptr/U64/I64/I32/F32/String/Utf16)
    - Evaluate chain and view result.

- **Settings**  
  Style editor + save/load to `%AppData%\MamboDMA\Examples\config.json`.

---

## Style, config & fonts

- Fonts are loaded in `OverlayWindow.SetupImGuiStyleAndFonts()`:
  ```csharp
  Fonts.Regular = io.Fonts.AddFontFromFileTTF("Assets/Fonts/AlanSans-Regular.ttf", 16f, cfg);
  // Medium, Bold …
  rlImGui.ReloadFonts();
  ```

- Style is persisted via `StyleConfig.Save/Load` and editable in the **Settings** tab.

---

## Common patterns / recipes

### Start/stop lifecycle
```csharp
static void Main(string[] args)
{
    JobSystem.Start(workers: 3);

    var (title, draw) = ("MamboDMA · Advanced", OverlayUI.Draw);
    using var win = new OverlayWindow(title, 1100, 700);
    OverlayUI.OverlayWindowApi.Bind(win);

    try { win.Run(draw); }
    finally
    {
        try { VmmService.DisposeVmm(); } catch { }
        try { JobSystem.Stop(); }        catch { }
    }
}
```

### One-shot background job
```csharp
JobSystem.Schedule(() =>
{
    var list = DmaMemory.GetProcessList();
    Snapshots.Mutate(s => s with { Processes = list.ToArray(), Status = "Enumerated processes" });
});
```

### Periodic sampling with cancellation
```csharp
JobSystem.Schedule(async ct =>
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            // read a few things
            if (DmaMemory.IsAttached) {
                // example: quick direct reads or a ScatterRound
            }
        }
        catch (Exception ex)
        {
            Snapshots.Mutate(s => s with { Status = "Read error: " + ex.Message });
        }

        await Task.Delay(10, ct); // ~100 Hz; tune as needed
    }
});
```

### Safe UI updates from workers
- **Never** mutate ImGui state from workers.
- Always **publish** to a shared state (`Snapshots.Mutate`) and let the UI **read** it on the render thread.

---

## Troubleshooting capture/streaming

If your overlay is visible locally but **not in OBS/AnyDesk** when focused/fullscreen:

- **Elevation mismatch**: If your app runs **as Admin**, run OBS/AnyDesk as Admin too (UIPI prevents capture).
- **Independent Flip / Fullscreen Optimizations**:
  - Disable **Fullscreen Optimizations** on your EXE (Properties → Compatibility).
  - Windows 11 → System → Display → Graphics → **Change default graphics settings** → turn OFF “Optimizations for windowed games”.
  - Or don’t size the window to *exact* full-screen (subtract 1px) to keep it composited.
- Ensure you **don’t** set `WDA_EXCLUDEFROMCAPTURE`. The sample calls `SetWindowDisplayAffinity(hwnd, 0 /*WDA_NONE*/)` in `Misc.ApplyAll()`.

---

## Notes

- `DmaMemory` assumes the VMM device string `"fpga"`; adjust to your environment.
- `VmmService` is intentionally minimal; extend it with your own domain logic (entity reads, caches, anti-stall, etc.).
- `ScatterReadMap` (from `VmmSharpEx.Scatter`) is the most efficient way to fetch many addresses every frame/tick.

---

## Quick API cheatsheet

```csharp
// VMM
DmaMemory.InitOnly("fpga", applyMMap: true);
if (DmaMemory.TryAttachOnce("explorer.exe", out var err)) { /* success */ }
DmaMemory.Dispose();

// Process / modules
var procs = DmaMemory.GetProcessList();         // List<ProcEntry>
var mods  = DmaMemory.GetModules();             // List<ModuleInfo>

// Reads
bool ok = DmaMemory.Read(addr, out int v);
var s   = DmaMemory.ReadAsciiZ(strPtr, 64);

// Chains
if (DmaMemory.TryFollow(DmaMemory.Base, new ulong[]{0x10,0x28}, out var p)) {}

// Scatter
DmaMemory.ScatterRound(rd => {
  rd.Add(out ulong ptr, someVA);
  rd.Add(out int health, ptr + 0x2C);
});

// Jobs
JobSystem.Start(3);
JobSystem.Schedule(async ct => { /* loop work */ });
JobSystem.Stop();
```

---

### License / attribution

- UI: ImGui.NET + raylib-cs + rlImGui_cs  
- Backend: VmmSharpEx (and your DMA device/driver)

> Thanks to Lone for VmmSharpEx.
