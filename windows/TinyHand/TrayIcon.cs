using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TinyHand;

/// The notification-area icon: click to turn the pointer on or off, right-click for the menu.
internal sealed class TrayIcon : IDisposable
{
    public event Action? Toggle;
    public event Action? Clear;
    public event Action? ToggleSound;
    public event Action? OpenSettings;
    public event Action? Quit;
    public event Action<bool>? MenuOpen;

    private readonly NotifyIcon icon = new();
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem toggleItem = new();
    private readonly ToolStripMenuItem clearItem = new();
    private readonly ToolStripMenuItem soundItem = new("Tap sound");
    private Icon? current;

    public Func<bool>? IsOn { get; set; }
    public Func<bool>? IsClickThrough { get; set; }
    public Func<bool>? SoundOn { get; set; }

    public TrayIcon()
    {
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        toggleItem.Click += (_, _) => Toggle?.Invoke();
        clearItem.Click += (_, _) => Clear?.Invoke();
        soundItem.Click += (_, _) => ToggleSound?.Invoke();
        menu.Items.Add(toggleItem);
        menu.Items.Add(clearItem);
        menu.Items.Add(soundItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit TinyHand", null, (_, _) => Quit?.Invoke()));
        menu.Opening += (_, _) =>
        {
            Refresh();
            MenuOpen?.Invoke(true);
        };
        menu.Closed += (_, _) => MenuOpen?.Invoke(false);

        icon.ContextMenuStrip = menu;
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Toggle?.Invoke();
            }
        };
        UpdateIcon();
        icon.Visible = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(UpdateIcon);
            }
        };
    }

    public void Refresh()
    {
        bool on = IsOn?.Invoke() ?? false;
        toggleItem.Text = on ? "Turn pointer off" : "Turn pointer on";
        toggleItem.ShortcutKeyDisplayString = Preferences.ToggleShortcut.Display;
        clearItem.Text = "Clear drawing";
        clearItem.ShortcutKeyDisplayString = Preferences.ClearShortcut.Display;
        soundItem.Checked = SoundOn?.Invoke() ?? false;
    }

    public void UpdateIcon()
    {
        bool on = IsOn?.Invoke() ?? false;
        bool through = IsClickThrough?.Invoke() ?? false;
        Color color = through ? Color.FromArgb(255, 149, 0) : LightTaskbar() ? Color.FromArgb(28, 28, 30) : Color.White;
        Icon? next = MakeIcon(on ? "menubar_on@3x.png" : "menubar_off@3x.png", color);
        Icon? old = current;
        if (next != null)
        {
            icon.Icon = next;
            current = next;
        }
        if (old != null && old != next)
        {
            Native.DestroyIcon(old.Handle);
            old.Dispose();
        }
        string tip = through
            ? "TinyHand clicks go through — double-tap Shift to tap again"
            : on ? "TinyHand is on — click to turn off, right-click for the menu"
                 : "TinyHand — click to turn on, right-click for the menu";
        icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    /// Tooltip-style message from the tray icon.
    public void Notify(string title, string text)
    {
        icon.BalloonTipTitle = title;
        icon.BalloonTipText = text;
        icon.ShowBalloonTip(4000);
    }

    private static bool LightTaskbar()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    /// The menu bar glyphs are black template images; recolour them for the taskbar.
    private static Icon? MakeIcon(string asset, Color color)
    {
        string? path = Paths.Asset(asset);
        Bitmap? source = path != null ? new Bitmap(path) : null;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            if (source != null)
            {
                g.DrawImage(source, new Rectangle(0, 0, size, size));
            }
            else
            {
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, size / 4, size / 4, size / 2, size / 2);
            }
        }
        source?.Dispose();
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * size];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
        }
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        if (current != null)
        {
            Native.DestroyIcon(current.Handle);
            current.Dispose();
        }
    }
}
