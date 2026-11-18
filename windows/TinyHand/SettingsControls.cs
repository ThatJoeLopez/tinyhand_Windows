using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace TinyHand;

/// Colours for the settings and welcome windows, following the Windows app theme.
internal sealed class Theme
{
    public bool Dark { get; }
    public Brush Window { get; }
    public Brush Sidebar { get; }
    public Brush Card { get; }
    public Brush CardBorder { get; }
    public Brush Text { get; }
    public Brush Secondary { get; }
    public Brush Separator { get; }
    public Brush Hover { get; }
    public Brush Selected { get; }
    public Brush Control { get; }
    public Brush Accent { get; }
    public Brush AccentText { get; }
    public Brush Danger { get; }

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");

    private Theme(bool dark)
    {
        Dark = dark;
        Window = dark ? Ui.Brush(255, 32, 32, 32) : Ui.Brush(255, 243, 243, 243);
        Sidebar = dark ? Ui.Brush(255, 28, 28, 28) : Ui.Brush(255, 236, 236, 236);
        Card = dark ? Ui.Brush(255, 43, 43, 43) : Ui.Brush(255, 251, 251, 251);
        CardBorder = dark ? Ui.Brush(255, 29, 29, 29) : Ui.Brush(255, 229, 229, 229);
        Text = dark ? Ui.Brush(255, 255, 255, 255) : Ui.Brush(255, 27, 27, 27);
        Secondary = dark ? Ui.Brush(255, 170, 170, 170) : Ui.Brush(255, 96, 96, 96);
        Separator = dark ? Ui.Brush(255, 58, 58, 58) : Ui.Brush(255, 234, 234, 234);
        Hover = dark ? Ui.Brush(20, 255, 255, 255) : Ui.Brush(12, 0, 0, 0);
        Selected = dark ? Ui.Brush(30, 255, 255, 255) : Ui.Brush(20, 0, 0, 0);
        Control = dark ? Ui.Brush(255, 55, 55, 55) : Ui.Brush(255, 255, 255, 255);
        Accent = Ui.Brush(255, 135, 84, 242);
        AccentText = Ui.Brush(255, 255, 255, 255);
        Danger = Ui.Brush(255, 232, 72, 85);
    }

    public static Theme Current()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool light = key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        return new Theme(!light);
    }
}

internal static class Controls
{
    public static TextBlock Label(Theme t, string text, double size = 13, FontWeight? weight = null, Brush? brush = null) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = Theme.Font,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? t.Text,
        TextWrapping = TextWrapping.Wrap,
    };

    /// A titled group of rows on a rounded card.
    public static FrameworkElement Card(Theme t, string? title, params UIElement[] rows)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        if (title != null)
        {
            TextBlock heading = Label(t, title, 12, FontWeights.SemiBold, t.Secondary);
            heading.Margin = new Thickness(4, 0, 0, 8);
            outer.Children.Add(heading);
        }
        var inner = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                inner.Children.Add(new Border { Height = 1, Background = t.Separator, Margin = new Thickness(14, 0, 14, 0) });
            }
            inner.Children.Add(rows[i]);
        }
        outer.Children.Add(new Border
        {
            Background = t.Card,
            BorderBrush = t.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = inner,
        });
        return outer;
    }

    /// A label (and optional detail) on the left, a control on the right.
    public static FrameworkElement Row(Theme t, string title, string? detail, UIElement trailing)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 14, 12), MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(Label(t, title, 13));
        if (detail != null)
        {
            TextBlock d = Label(t, detail, 11.5, brush: t.Secondary);
            d.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(d);
        }
        grid.Children.Add(text);
        if (trailing is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(trailing);
        return grid;
    }

    /// An on/off switch.
    public static FrameworkElement Toggle(Theme t, bool on, Action<bool> changed, bool enabled = true)
    {
        var knob = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = on ? t.AccentText : t.Secondary,
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 4, 0),
        };
        var track = new Border
        {
            Width = 40,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = on ? t.Accent : Brushes.Transparent,
            BorderBrush = on ? t.Accent : t.Secondary,
            BorderThickness = new Thickness(1),
            Child = knob,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Opacity = enabled ? 1 : 0.4,
            Focusable = true,
        };
        System.Windows.Automation.AutomationProperties.SetName(track, on ? "On" : "Off");
        if (enabled)
        {
            track.MouseLeftButtonUp += (_, _) => changed(!on);
            track.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Space or Key.Enter)
                {
                    changed(!on);
                }
            };
        }
        return track;
    }

    /// A row of options where one is chosen.
    public static FrameworkElement Segmented<T>(Theme t, IEnumerable<(string title, T value)> options, T selected, Action<T> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var outer = new Border
        {
            Background = t.Dark ? Ui.Brush(255, 30, 30, 30) : Ui.Brush(255, 232, 232, 232),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(2),
            Child = row,
        };
        foreach ((string title, T value) in options)
        {
            bool isOn = EqualityComparer<T>.Default.Equals(value, selected);
            var b = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 5, 12, 5),
                Background = isOn ? t.Control : Brushes.Transparent,
                Child = Label(t, title, 12, isOn ? FontWeights.SemiBold : FontWeights.Normal),
                Cursor = Cursors.Hand,
            };
            if (isOn)
            {
                b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0.5, Opacity = 0.15, Direction = 270 };
            }
            T v = value;
            b.MouseLeftButtonUp += (_, _) => changed(v);
            row.Children.Add(b);
        }
        return outer;
    }

    public static Border Button(Theme t, string title, Action action, bool primary = false, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        Brush fg = primary ? t.AccentText : t.Text;
        if (glyph != null)
        {
            TextBlock icon = Ui.Icon(glyph, 12, fg);
            icon.Margin = new Thickness(0, 0, 7, 0);
            content.Children.Add(icon);
        }
        content.Children.Add(Label(t, title, 12.5, FontWeights.SemiBold, fg));
        var b = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 6, 14, 6),
            Background = primary ? t.Accent : t.Control,
            BorderBrush = primary ? t.Accent : t.CardBorder,
            BorderThickness = new Thickness(1),
            Child = content,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        b.MouseEnter += (_, _) => b.Opacity = 0.88;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        // Claim the press, so a draggable parent (like the welcome card) doesn't start a
        // window drag that swallows the release.
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return b;
    }

    public static TextBlock TextButton(Theme t, string title, Action action, Brush? brush = null)
    {
        TextBlock b = Label(t, title, 12.5, FontWeights.SemiBold, brush ?? t.Accent);
        b.Cursor = Cursors.Hand;
        b.TextWrapping = TextWrapping.NoWrap;
        b.MouseEnter += (_, _) => b.TextDecorations = TextDecorations.Underline;
        b.MouseLeave += (_, _) => b.TextDecorations = null;
        // Claim the press, so a draggable parent (like the welcome card) doesn't start a
        // window drag that swallows the release.
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return b;
    }

    /// Shows key caps, like Ctrl + Alt + H.
    public static FrameworkElement KeyCaps(Theme t, IEnumerable<string> keys)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (string k in keys)
        {
            row.Children.Add(new Border
            {
                Background = t.Dark ? Ui.Brush(255, 70, 70, 70) : Ui.Brush(255, 255, 255, 255),
                BorderBrush = t.CardBorder,
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(2, 0, 2, 0),
                Child = Label(t, k, 11.5, FontWeights.SemiBold),
            });
        }
        return row;
    }
}
