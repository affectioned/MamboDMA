using System;
using MamboDMA.Services;
using static MamboDMA.OverlayUI;

namespace MamboDMA;

internal static class Program
{
    private enum UiChoice { Advanced, Simple }

    private static UiChoice AskUiChoice(string[] args)
    {
        // 1) command-line override
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim().ToLowerInvariant();
            if (a is "--ui" or "-u")
            {
                if (i + 1 < args.Length)
                {
                    var val = args[i + 1].Trim().ToLowerInvariant();
                    if (val.StartsWith("adv")) return UiChoice.Advanced;
                    if (val.StartsWith("simp")) return UiChoice.Simple;
                }
            }
            else if (a is "advanced" or "adv") return UiChoice.Advanced;
            else if (a is "simple" or "simp") return UiChoice.Simple;
        }

        // 2) interactive prompt
        Console.WriteLine("Choose UI:");
        Console.WriteLine("  [1] OverlayUI (Advanced Example)");
        Console.WriteLine("  [2] ServiceDemoUI (Simple Example)");
        Console.Write("Enter 1 or 2 (default = 1): ");
        var input = Console.ReadLine()?.Trim();

        return input == "2" ? UiChoice.Simple : UiChoice.Advanced;
    }

    private static (string title, Action draw) ResolveUi(UiChoice choice)
    {
        switch (choice)
        {
            case UiChoice.Simple:
                return ("MamboDMA · Simple", ServiceDemoUI.Draw);
            case UiChoice.Advanced:
            default:
                return ("MamboDMA · Advanced", OverlayUI.Draw);
        }
    }

    private static void Main(string[] args)
    {
        JobSystem.Start(workers: 3);

        var choice = AskUiChoice(args);
        var (title, drawLoop) = ResolveUi(choice);

        using var win = new OverlayWindow(title, 1100, 700);
        OverlayWindowApi.Bind(win);

        try
        {
            win.Run(drawLoop);   // returns when you click the in-UI “X” (OverlayWindowApi.Quit)
        }
        finally
        {
            try { VmmService.DisposeVmm(); } catch { }
            try { JobSystem.Stop(); } catch { }
        }
    }
}
