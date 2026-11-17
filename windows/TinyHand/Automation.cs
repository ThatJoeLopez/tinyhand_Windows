using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TinyHand;

/// Handles tinyhand:// links and mirrors the app state to a JSON file, so launchers like
/// PowerToys Run, Flow Launcher or a script can drive TinyHand and read what it is doing.
internal sealed class Automation
{
    public static string StatePath => Path.Combine(Paths.Data, "state.json");

    private readonly OverlayController overlay;

    public Automation(OverlayController overlay)
    {
        this.overlay = overlay;
        CustomHands.Changed += WriteState;
    }

    public void Handle(string link)
    {
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out Uri? uri) || !uri.Scheme.Equals("tinyhand", StringComparison.OrdinalIgnoreCase))
        {
            Log.Write($"ignoring argument: {link}");
            return;
        }
        string command = uri.Host.ToLowerInvariant();
        string? argument = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        Log.Write($"url command: {command} {argument}");

        switch (command)
        {
            case "on":
                overlay.Activate();
                break;
            case "off":
                overlay.Deactivate();
                break;
            case "toggle":
                overlay.Toggle();
                break;
            case "clear":
                overlay.ClearInk();
                break;
            case "style":
                SelectStyle(argument);
                break;
            case "sound":
                SetSound(argument);
                break;
            default:
                Log.Write($"unknown url command: {command}");
                break;
        }
    }

    private void SelectStyle(string? id)
    {
        if (id == null || PointerStyle.FromId(Uri.UnescapeDataString(id)) is not PointerStyle style)
        {
            Log.Write($"unknown pointer style: {id ?? "none"}");
            return;
        }
        overlay.Pick(style);
        overlay.Activate();
    }

    private void SetSound(string? value)
    {
        switch (value)
        {
            case "on":
                overlay.SoundOn = true;
                break;
            case "off":
                overlay.SoundOn = false;
                break;
            case "toggle" or null:
                overlay.SoundOn = !overlay.SoundOn;
                break;
            default:
                Log.Write($"unknown sound value: {value}");
                break;
        }
    }

    public void WriteState()
    {
        var state = new
        {
            active = overlay.IsActive,
            pid = Environment.ProcessId,
            soundOn = overlay.SoundOn,
            style = overlay.Style.Id,
            styles = PointerStyle.AllCases.Select(s => new
            {
                id = s.Id,
                name = s.Title,
                type = s.IsLaser ? "laser" : s.IsCustom ? "custom" : "hand",
            }),
        };
        try
        {
            Directory.CreateDirectory(Paths.Data);
            string tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, StatePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"state write failed: {e.Message}");
        }
    }

    public static void RemoveState()
    {
        try
        {
            File.Delete(StatePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// Points tinyhand:// at this exe, for the current user only.
    public static void RegisterProtocol()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\tinyhand");
            key.SetValue("", "URL:TinyHand");
            key.SetValue("URL Protocol", "");
            using RegistryKey icon = key.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{Paths.ExePath}\",0");
            using RegistryKey command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{Paths.ExePath}\" \"%1\"");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Write($"could not register tinyhand:// links: {e.Message}");
        }
    }
}

/// Keeps one TinyHand per user session. A second launch hands its arguments to the first over
/// a named pipe and exits, which is how `start tinyhand://toggle` reaches the running app.
internal sealed class SingleInstance : IDisposable
{
    private static string Id => $"TinyHand-{Environment.UserName}-{Environment.UserDomainName}".Replace('\\', '-');

    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();

    public bool IsFirst { get; }

    public SingleInstance()
    {
        mutex = new Mutex(initiallyOwned: true, $"Local\\{Id}", out bool created);
        IsFirst = created;
    }

    /// Sends arguments to the running instance. Returns false if it couldn't be reached.
    public static bool Forward(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            client.Connect(2000);
            byte[] payload = Encoding.UTF8.GetBytes(string.Join("\n", args.Length == 0 ? new[] { "--show" } : args));
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Write($"could not reach the running TinyHand: {e.Message}");
            return false;
        }
    }

    public void Listen(Dispatcher ui, Action<string[]> received)
    {
        var thread = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    server.WaitForConnectionAsync(stop.Token).GetAwaiter().GetResult();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string[] args = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    ui.BeginInvoke(() => received(args));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException e)
                {
                    Log.Write($"command pipe error: {e.Message}");
                    Thread.Sleep(200);
                }
            }
        })
        {
            IsBackground = true,
            Name = "TinyHand commands",
        };
        thread.Start();
    }

    public void Dispose()
    {
        stop.Cancel();
        if (IsFirst)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }
        mutex.Dispose();
    }
}
