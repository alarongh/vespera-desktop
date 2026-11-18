using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Vespera;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var instance = new Mutex(true, @"Local\VesperaDesktopOverlay", out bool firstInstance);
        if (!firstInstance && !args.Contains("--check")) return 0;
        try
        {
            string artwork = Path.Combine(AppContext.BaseDirectory, "artwork");
            AnimationPack pack = AnimationPack.Load(artwork);
            ActivityRules rules = ActivityRules.Load(artwork);
            if (args.Contains("--check"))
            {
                pack.CheckPlayback();
                rules.CheckPriority();
                DragPlayback.Check();
                TypingDance.Check(rules);
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new CompanionWindow(pack, rules, args.Contains("--demo") || args.Contains("--smoke-test"));
            if (args.Contains("--smoke-test"))
            {
                window.Opacity = 0;
                var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                stop.Tick += (_, _) => { stop.Stop(); window.Close(); };
                stop.Start();
            }
            return app.Run(window);
        }
        catch (Exception error)
        {
            if (args.Contains("--check") || args.Contains("--smoke-test"))
                Trace.WriteLine(error);
            else
                MessageBox.Show(error.Message, "Веспера", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
