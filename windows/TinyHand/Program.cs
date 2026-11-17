using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace TinyHand;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Tools that don't need the running app.
        int i = Array.IndexOf(args, "--make-hand");
        if (i >= 0 && args.Length > i + 2)
        {
            return MakeHand(args[i + 1], args[i + 2]);
        }

        using var instance = new SingleInstance();
        if (!instance.IsFirst)
        {
            // Another TinyHand is running: hand it our links and leave.
            return SingleInstance.Forward(args) ? 0 : 1;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write($"crash: {e.ExceptionObject}");
            CursorHider.Show();
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"unhandled: {e.Exception}");
            e.Handled = true;
        };

        if (args.Contains("--click-test"))
        {
            var overlay = new OverlayController(app.Dispatcher);
            string report = overlay.RunClickTest();
            File.WriteAllText(Path.Combine(Paths.LocalData, "click-test.txt"), report);
            Console.WriteLine(report);
            return 0;
        }

        AppController? controller = null;
        app.Startup += (_, _) =>
        {
            controller = new AppController(app.Dispatcher, args);
            instance.Listen(app.Dispatcher, controller.HandleArguments);
        };
        app.Exit += (_, _) => controller?.Shutdown();
        return app.Run();
    }

    private static int MakeHand(string input, string output)
    {
        try
        {
            (var image, uint ink) = HandMaker.Make(input);
            HandMaker.Write(image, output);
            Console.WriteLine($"ok {ink:X6}");
            return 0;
        }
        catch (HandMaker.Problem p)
        {
            Console.WriteLine($"rejected: {p.Message}");
            return 1;
        }
        catch (Exception e)
        {
            Console.WriteLine($"failed: {e.Message}");
            return 1;
        }
    }
}
