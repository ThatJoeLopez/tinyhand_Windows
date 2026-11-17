using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TinyHand;

/// A small card shown on first launch. Windows needs no permission for TinyHand, so it just
/// explains how to start and where the app lives.
internal sealed class WelcomeWindow : Window
{
    public event Action? TryIt;

    public WelcomeWindow()
    {
        Theme t = Theme.Current();
        Title = "Welcome to TinyHand";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = true;
        FontFamily = Theme.Font;
        Icon = SettingsWindow.AppIcon();

        var stack = new StackPanel { Width = 300, Margin = new Thickness(24, 26, 24, 22) };
        stack.Children.Add(new Image { Source = SettingsWindow.AppIcon(), Width = 72, Height = 72, Margin = new Thickness(0, 0, 0, 12) });
        TextBlock title = Controls.Label(t, "You're all set", 20, FontWeights.Bold);
        title.TextAlignment = TextAlignment.Center;
        stack.Children.Add(title);
        TextBlock subtitle = Controls.Label(t,
            $"Press {Preferences.ToggleShortcut.Display} any time to bring out the hand, or click the hand in the notification area of the taskbar.",
            12.5, brush: t.Secondary);
        subtitle.TextAlignment = TextAlignment.Center;
        subtitle.Margin = new Thickness(0, 6, 0, 16);
        stack.Children.Add(subtitle);

        var tips = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        foreach ((string key, string text) in new[]
        {
            ("Click", "to tap — nothing underneath gets clicked"),
            ("Drag", "to draw, hold Ctrl for an arrow"),
            ($"Hold {Preferences.InteractKey.Title()}", "to click for real"),
            ("Esc", "to put the hand away"),
        })
        {
            if (key.EndsWith("Off", StringComparison.Ordinal))
            {
                continue;
            }
            var line = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 2, 0, 2), Foreground = t.Text, TextWrapping = TextWrapping.Wrap };
            line.Inlines.Add(new System.Windows.Documents.Run(key) { FontWeight = FontWeights.SemiBold });
            line.Inlines.Add(new System.Windows.Documents.Run(" " + text) { Foreground = t.Secondary });
            tips.Children.Add(line);
        }
        stack.Children.Add(tips);

        Border start = Controls.Button(t, "Try it", () =>
        {
            Close();
            TryIt?.Invoke();
        }, primary: true);
        start.HorizontalAlignment = HorizontalAlignment.Stretch;
        ((StackPanel)start.Child).HorizontalAlignment = HorizontalAlignment.Center;
        start.Padding = new Thickness(14, 9, 14, 9);
        stack.Children.Add(start);
        TextBlock later = Controls.TextButton(t, "Later", Close, t.Secondary);
        later.HorizontalAlignment = HorizontalAlignment.Center;
        later.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(later);

        var card = new Border
        {
            Background = t.Card,
            BorderBrush = t.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = stack,
            Margin = new Thickness(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.3 },
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (!e.Handled && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
        Content = card;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }
}
