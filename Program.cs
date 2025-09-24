using System;
using MamboDMA.Input;
using static MamboDMA.DmaMemory;

namespace MamboDMA;

internal static class Program
{
    private static readonly CancellationTokenSource _appCts = new();
    private static void Main()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, __) => DmaMemory.Dispose();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; DmaMemory.Dispose(); };

        using var win = new OverlayWindow("MamboDMA", 1100, 700);
        win.Run(OverlayUI.Draw);
    }
}
