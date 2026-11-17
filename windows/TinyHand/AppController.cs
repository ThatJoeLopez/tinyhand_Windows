using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace TinyHand;

/// Wires the tray icon, hotkeys, settings, welcome card and automation to the overlay.
internal sealed class AppController
{
    private readonly Dispatcher ui;
    private readonly OverlayController overlay;
    private readonly TrayIcon tray = new();
    private readonly Automation automation;
    private SettingsWindow? settings;
    private WelcomeWindow? welcome;
    private int? toggleHotKey;
    private int? clearHotKey;

    public AppController(Dispatcher ui, string[] args)
    {
        this.ui = ui;
        CursorHider.RecoverFromCrash();
        Log.Write($"launch {Paths.ExePath}");
        Automation.RegisterProtocol();
        Preferences.RefreshLaunchAtLoginPath();
        EnableLaunchAtLoginOnFirstRun();

        overlay = new OverlayController(ui);
        automation = new Automation(overlay);
        HandAsset.PreloadThumbnails(PointerStyle.AllCases.Select(s => s.AssetName).OfType<string>());

        overlay.ActiveChanged += () =>
        {
            tray.UpdateIcon();
            automation.WriteState();
        };
        overlay.SoundChanged += () =>
        {
            tray.Refresh();
            automation.WriteState();
        };

        tray.IsOn = () => overlay.IsActive;
        tray.IsClickThrough = () => overlay.IsClickThrough;
        tray.SoundOn = () => overlay.SoundOn;
        tray.Toggle += overlay.Toggle;
        tray.Clear += overlay.ClearInk;
        tray.ToggleSound += () => overlay.SoundOn = !overlay.SoundOn;
        tray.OpenSettings += () => ShowSettings();
        tray.Quit += Quit;
        tray.MenuOpen += open => overlay.SuspendInput(open);
        tray.UpdateIcon();

        if (!RegisterHotKeys())
        {
            tray.Notify("TinyHand", $"{Preferences.ToggleShortcut.Display} or {Preferences.ClearShortcut.Display} is taken by another app. Pick another in Settings.");
        }

        automation.WriteState();

        bool background = args.Contains("--background");
        string[] links = args.Where(a => a.StartsWith("tinyhand:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!Preferences.FlagSet("welcomeShown") && !background && links.Length == 0)
        {
            ShowWelcome();
        }
        foreach (string link in links)
        {
            automation.Handle(link);
        }
    }

    /// Arguments forwarded from a second launch.
    public void HandleArguments(string[] args)
    {
        bool handled = false;
        foreach (string arg in args)
        {
            if (arg.StartsWith("tinyhand:", StringComparison.OrdinalIgnoreCase))
            {
                automation.Handle(arg);
                handled = true;
            }
        }
        if (!handled && !args.Contains("--background"))
        {
            // Launching TinyHand again (from Start, say) opens Settings, so it's easy to find.
            ShowSettings();
        }
    }

    public void Shutdown()
    {
        overlay.Deactivate();
        CursorHider.Show();
        Automation.RemoveState();
        tray.Dispose();
        HotKeys.Shared.Dispose();
    }

    private void Quit()
    {
        overlay.Deactivate();
        Application.Current.Shutdown();
    }

    private void ShowSettings(SettingsSection? section = null)
    {
        overlay.Exit();
        if (settings == null)
        {
            settings = new SettingsWindow
            {
                ApplyToggleShortcut = s => Apply(s, isToggle: true),
                ApplyClearShortcut = s => Apply(s, isToggle: false),
                OnRecording = recording =>
                {
                    if (recording)
                    {
                        UnregisterHotKeys();
                    }
                    else
                    {
                        RegisterHotKeys();
                    }
                },
            };
            settings.Closed += (_, _) =>
            {
                RegisterHotKeys();
                settings = null;
            };
        }
        settings.ShowSection(section);
    }

    private void ShowWelcome()
    {
        Preferences.SetFlag("welcomeShown");
        if (welcome == null)
        {
            welcome = new WelcomeWindow();
            welcome.TryIt += () => overlay.Activate();
            welcome.Closed += (_, _) => welcome = null;
        }
        welcome.Show();
        welcome.Activate();
    }

    private bool Apply(Shortcut s, bool isToggle)
    {
        UnregisterHotKeys();
        Shortcut old = isToggle ? Preferences.ToggleShortcut : Preferences.ClearShortcut;
        if (isToggle)
        {
            Preferences.ToggleShortcut = s;
        }
        else
        {
            Preferences.ClearShortcut = s;
        }
        if (RegisterHotKeys())
        {
            return true;
        }
        UnregisterHotKeys();
        if (isToggle)
        {
            Preferences.ToggleShortcut = old;
        }
        else
        {
            Preferences.ClearShortcut = old;
        }
        RegisterHotKeys();
        return false;
    }

    private bool RegisterHotKeys()
    {
        toggleHotKey ??= HotKeys.Shared.Register(Preferences.ToggleShortcut, overlay.Toggle);
        clearHotKey ??= HotKeys.Shared.Register(Preferences.ClearShortcut, overlay.ClearInk);
        if (toggleHotKey == null || clearHotKey == null)
        {
            Log.Write($"hotkey registration failed: toggle={toggleHotKey != null} clear={clearHotKey != null}");
        }
        return toggleHotKey != null && clearHotKey != null;
    }

    private void UnregisterHotKeys()
    {
        if (toggleHotKey is int t)
        {
            HotKeys.Shared.Unregister(t);
        }
        if (clearHotKey is int c)
        {
            HotKeys.Shared.Unregister(c);
        }
        toggleHotKey = null;
        clearHotKey = null;
    }

    private static void EnableLaunchAtLoginOnFirstRun()
    {
        const string key = "loginItemSetUp";
        if (Preferences.FlagSet(key))
        {
            return;
        }
        Preferences.SetFlag(key);
        // Only when TinyHand looks installed, not when run from a build folder or Downloads.
        string exe = Paths.ExePath;
        bool installed = exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
            || exe.StartsWith(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), StringComparison.OrdinalIgnoreCase);
        if (installed)
        {
            Preferences.LaunchAtLogin = true;
            Log.Write($"first run: open at login = {Preferences.LaunchAtLogin}");
        }
    }
}
