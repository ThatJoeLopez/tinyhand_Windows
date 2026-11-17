using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TinyHand;

/// The strip of pointers, ink, sound and exit buttons along the bottom of the screen.
internal sealed class Toolbar
{
    public event Action<PointerStyle>? Pick;
    public event Action? ToggleSound;
    public event Action? Exit;

    private readonly FloatingWindow panel = new() { SizeToContent = SizeToContent.WidthAndHeight, Opacity = 0 };
    private readonly StackPanel stack = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<PointerStyle, Border> styleButtons = new();
    private List<PointerStyle> shownStyles = new();
    private PointerStyle? current;
    private readonly Border inkButton;
    private readonly Border soundButton;
    private readonly Image inkImage = new() { Width = 40, Height = 40 };
    private readonly TextBlock soundGlyph;
    private readonly Dictionary<InkChoice, Border> swatches = new();
    private bool inkOpen;

    private readonly FloatingWindow tray = new() { SizeToContent = SizeToContent.WidthAndHeight, Opacity = 0 };
    private readonly StackPanel trayStack = new() { Orientation = Orientation.Horizontal };
    private FloatingWindow? hint;

    private (Native.RECT bounds, Native.RECT work) screen;

    public bool Revealed { get; private set; }

    /// The toolbar (and ink tray, if open) in physical pixels.
    public Native.RECT Frame
    {
        get
        {
            Native.RECT f = panel.PixelBounds;
            if (inkOpen)
            {
                Native.RECT t = tray.PixelBounds;
                f = new Native.RECT
                {
                    Left = Math.Min(f.Left, t.Left),
                    Top = Math.Min(f.Top, t.Top),
                    Right = Math.Max(f.Right, t.Right),
                    Bottom = Math.Max(f.Bottom, t.Bottom),
                };
            }
            return f;
        }
    }

    public IEnumerable<FloatingWindow> Windows
    {
        get
        {
            yield return panel;
            if (tray.IsVisible) yield return tray;
            if (hint != null) yield return hint;
        }
    }

    public Toolbar()
    {
        panel.Content = Ui.DarkGlass(16, stack, new Thickness(10, 8, 10, 8));
        panel.ClickThrough = true;
        tray.Content = Ui.DarkGlass(14, trayStack, new Thickness(6, 5, 6, 5));

        stack.Children.Add(Divider());
        inkButton = MakeButton(inkImage, "Ink colour", 40, () => SetInkOpen(!inkOpen));
        stack.Children.Add(inkButton);
        soundGlyph = Ui.Icon(Ui.GlyphVolume, 16, Ui.White(0.85));
        soundButton = MakeButton(soundGlyph, "Tap sound", 40, () => ToggleSound?.Invoke());
        stack.Children.Add(soundButton);
        stack.Children.Add(MakeButton(Ui.Icon(Ui.GlyphClose, 14, Ui.White(0.85)), "Exit  (Esc)", 40, () => Exit?.Invoke()));

        RebuildStyles();
        RefreshInk();
        Preferences.Changed += () =>
        {
            RebuildStyles();
            RefreshInk();
        };
        CustomHands.Changed += RebuildStyles;
    }

    private void SetInkOpen(bool open)
    {
        if (open == inkOpen)
        {
            return;
        }
        inkOpen = open;
        if (open)
        {
            if (swatches.Count == 0)
            {
                foreach (InkChoice choice in Enum.GetValues<InkChoice>())
                {
                    var image = new Image { Width = 30, Height = 30 };
                    Border b = MakeButton(image, choice.Title(), 30, () =>
                    {
                        Preferences.InkChoice = choice;
                        SetInkOpen(false);
                    });
                    b.CornerRadius = new CornerRadius(15);
                    swatches[choice] = b;
                    trayStack.Children.Add(b);
                }
            }
            RefreshInk();
            tray.ShowQuietly();
            tray.UpdateLayout();
            Native.RECT ink = ButtonPixels(inkButton);
            Native.RECT p = panel.PixelBounds;
            Native.RECT t = tray.PixelBounds;
            double scale = panel.DpiScale;
            tray.MoveToPixels((ink.Left + ink.Right) / 2 - t.Width / 2, p.Top - t.Height + (int)(12 * scale));
            tray.FadeTo(1, 0.12);
        }
        else
        {
            tray.FadeTo(0, 0.12, () =>
            {
                if (!inkOpen)
                {
                    tray.Hide();
                }
            });
        }
        RefreshInk();
    }

    private Native.RECT ButtonPixels(FrameworkElement button)
    {
        Point topLeft = button.PointToScreen(new Point(0, 0));
        Point bottomRight = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
        return new Native.RECT { Left = (int)topLeft.X, Top = (int)topLeft.Y, Right = (int)bottomRight.X, Bottom = (int)bottomRight.Y };
    }

    private void RefreshInk()
    {
        PointerStyle style = current ?? PointerStyle.Saved;
        inkImage.Source = Ui.InkDot(Preferences.InkChoice.Resolved(style));
        inkButton.Background = inkOpen ? Ui.White(0.18) : Brushes.Transparent;
        Visibility visibility = Preferences.ShowInkButton ? Visibility.Visible : Visibility.Collapsed;
        if (inkButton.Visibility != visibility)
        {
            inkButton.Visibility = visibility;
            if (visibility != Visibility.Visible)
            {
                SetInkOpen(false);
            }
            KeepCentered();
        }
        foreach ((InkChoice choice, Border b) in swatches)
        {
            ((Image)b.Child).Source = choice.Color() is Color c ? Ui.InkDot(c) : Ui.RainbowDot();
            b.Background = choice == Preferences.InkChoice ? Ui.White(0.2) : Brushes.Transparent;
        }
    }

    private void RebuildStyles()
    {
        List<PointerStyle> styles = Preferences.PinnedStyles;
        if (current != null && !styles.Contains(current))
        {
            styles.Add(current);
        }
        if (styles.SequenceEqual(shownStyles))
        {
            return;
        }
        shownStyles = styles;
        foreach (Border b in styleButtons.Values)
        {
            stack.Children.Remove(b);
        }
        styleButtons.Clear();
        for (int i = 0; i < styles.Count; i++)
        {
            PointerStyle style = styles[i];
            var image = new Image { Source = Ui.StyleIcon(style), Width = 34, Height = 34 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Border b = MakeButton(image, style.Title, 40, () => Pick?.Invoke(style));
            styleButtons[style] = b;
            stack.Children.Insert(i, b);
        }
        if (current != null)
        {
            Highlight(current);
        }
        KeepCentered();
    }

    public void SetSoundOn(bool on)
    {
        soundGlyph.Text = on ? Ui.GlyphVolume : Ui.GlyphMute;
        soundGlyph.Foreground = Ui.White(on ? 0.85 : 0.45);
        soundButton.ToolTip = on ? "Tap sound on — click to mute" : "Tap sound off — click to unmute";
    }

    public Point? CenterOf(PointerStyle style)
    {
        if (!styleButtons.TryGetValue(style, out Border? b) || !panel.IsVisible)
        {
            return null;
        }
        Native.RECT r = ButtonPixels(b);
        return new Point((r.Left + r.Right) / 2.0, (r.Top + r.Bottom) / 2.0);
    }

    public void ShowHint(IEnumerable<(string key, string text)> parts)
    {
        HideHint();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        bool first = true;
        foreach ((string key, string text) in parts)
        {
            var block = new TextBlock
            {
                FontSize = 12.5,
                Margin = new Thickness(first ? 0 : 14, 0, 0, 0),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            };
            block.Inlines.Add(new Run(key) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            block.Inlines.Add(new Run(" " + text) { Foreground = Ui.White(0.7) });
            row.Children.Add(block);
            first = false;
        }
        var w = new FloatingWindow
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            Opacity = 0,
            Content = Ui.DarkGlass(12, row, new Thickness(16, 9, 16, 9)),
            ClickThrough = true,
        };
        w.ShowQuietly();
        w.UpdateLayout();
        Native.RECT p = panel.IsVisible ? panel.PixelBounds : Bottom();
        Native.RECT h = w.PixelBounds;
        w.MoveToPixels((p.Left + p.Right) / 2 - h.Width / 2, p.Top - h.Height + (int)(8 * w.DpiScale));
        w.FadeTo(1, 0.2);
        hint = w;
    }

    private Native.RECT Bottom()
    {
        int cx = (screen.bounds.Left + screen.bounds.Right) / 2;
        return new Native.RECT { Left = cx, Right = cx, Top = screen.work.Bottom - 70, Bottom = screen.work.Bottom - 14 };
    }

    public void HideHint()
    {
        FloatingWindow? w = hint;
        if (w == null)
        {
            return;
        }
        hint = null;
        w.FadeTo(0, 0.3, w.Close);
    }

    public void HideHint(TimeSpan after)
    {
        FloatingWindow? shown = hint;
        var timer = new DispatcherTimer { Interval = after };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (hint == shown)
            {
                HideHint();
            }
        };
        timer.Start();
    }

    public void Select(PointerStyle style)
    {
        current = style;
        RebuildStyles();
        Highlight(style);
        RefreshInk();
    }

    private void Highlight(PointerStyle style)
    {
        foreach ((PointerStyle s, Border b) in styleButtons)
        {
            b.Background = s == style ? Ui.White(0.18) : Brushes.Transparent;
            b.Tag = s == style;
        }
    }

    /// Centres the toolbar at the bottom of the given monitor's work area (physical pixels).
    public void Place(Native.RECT bounds, Native.RECT work)
    {
        SetInkOpen(false);
        screen = (bounds, work);
        if (!panel.IsVisible)
        {
            return;
        }
        panel.UpdateLayout();
        Native.RECT f = panel.PixelBounds;
        double scale = panel.DpiScale;
        // The window has a 10 DIP margin for its shadow.
        int x = (bounds.Left + bounds.Right) / 2 - f.Width / 2;
        int y = work.Bottom - f.Height - (int)((14 - 10) * scale);
        panel.MoveToPixels(x, y);
    }

    private void KeepCentered()
    {
        if (!panel.IsVisible)
        {
            return;
        }
        panel.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Place(screen.bounds, screen.work));
    }

    public void Show()
    {
        panel.ShowQuietly();
        Place(screen.bounds, screen.work);
        // A move onto a monitor with another scale factor resizes the window; place it again after.
        panel.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Place(screen.bounds, screen.work));
    }

    public void Hide()
    {
        SetInkOpen(false);
        tray.Hide();
        HideHint();
        Revealed = false;
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        panel.Opacity = 0;
        panel.ClickThrough = true;
        panel.Hide();
    }

    public void SetRevealed(bool value)
    {
        if (value == Revealed)
        {
            return;
        }
        Revealed = value;
        if (!value)
        {
            SetInkOpen(false);
        }
        panel.ClickThrough = !value;
        panel.FadeTo(value ? 1 : 0, value ? 0.15 : 0.35);
    }

    public void RaiseToTop()
    {
        foreach (FloatingWindow w in Windows)
        {
            w.RaiseToTop();
        }
    }

    private static Border MakeButton(UIElement content, string tip, double side, Action action)
    {
        var b = new Border
        {
            Width = side,
            Height = side,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Margin = new Thickness(3, 0, 3, 0),
            Child = content,
            ToolTip = tip,
            Cursor = Cursors.Hand,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.MouseEnter += (_, _) =>
        {
            if (b.Tag is not true)
            {
                b.Background = Ui.White(0.1);
            }
        };
        b.MouseLeave += (_, _) =>
        {
            if (b.Tag is not true && b.Background is SolidColorBrush s && s.Color.A == 25)
            {
                b.Background = Brushes.Transparent;
            }
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return b;
    }

    private static UIElement Divider() => new Border
    {
        Width = 1,
        Height = 24,
        Margin = new Thickness(5, 0, 5, 0),
        Background = Ui.White(0.18),
        VerticalAlignment = VerticalAlignment.Center,
    };
}
