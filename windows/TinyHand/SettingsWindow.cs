using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TinyHand;

internal enum SettingsSection
{
    General,
    Pointers,
    Appearance,
    Drawing,
    Sound,
    About,
}

/// The Settings window, built in code. Each change to a setting rebuilds the visible page.
internal sealed class SettingsWindow : Window
{
    public Func<Shortcut, bool>? ApplyToggleShortcut { get; set; }
    public Func<Shortcut, bool>? ApplyClearShortcut { get; set; }
    public Action<bool>? OnRecording { get; set; }

    private Theme theme = Theme.Current();
    private SettingsSection section = SettingsSection.General;
    private readonly Grid root = new();
    private readonly StackPanel sidebar = new() { Margin = new Thickness(10, 16, 10, 10) };
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(28, 24, 28, 24) };
    private bool quiet;
    private bool rebuildQueued;

    private string? shortcutError;
    private string? pinMessage;
    private string? handMessage;
    private bool recordingToggle;
    private bool recordingClear;

    // Making a custom hand.
    private bool makingHand;
    private string handIdea = "";
    private string handName = "";
    private (BitmapSource image, uint ink)? draft;
    private string? draftError;
    private bool promptCopied;

    private TapSound? previewSound;

    private static readonly (string title, double seconds)[] FadeChoices =
    {
        ("3 s", 3), ("6 s", 6), ("10 s", 10), ("Keep", 0),
    };

    public SettingsWindow()
    {
        Title = "TinyHand Settings";
        Width = 820;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Font;
        UseLayoutRounding = true;
        Icon = AppIcon();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var side = new Border { Child = sidebar };
        root.Children.Add(side);
        Grid.SetColumn(scroller, 1);
        root.Children.Add(scroller);
        Content = root;

        Action changed = QueueRebuild;
        Preferences.Changed += changed;
        CustomHands.Changed += changed;
        Closed += (_, _) =>
        {
            Preferences.Changed -= changed;
            CustomHands.Changed -= changed;
            StopRecording();
            previewSound?.Dispose();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        HandAsset.PreloadThumbnails(PointerStyle.AllCases.Select(s => s.AssetName).OfType<string>(), QueueRebuild);
        Rebuild();
    }

    public static ImageSource? AppIcon()
    {
        try
        {
            return BitmapFrame.Create(new Uri("pack://application:,,,/TinyHand.ico", UriKind.Absolute));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void ShowSection(SettingsSection? s)
    {
        if (s is SettingsSection chosen)
        {
            section = chosen;
        }
        shortcutError = null;
        pinMessage = null;
        Rebuild();
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void QueueRebuild()
    {
        if (quiet || rebuildQueued)
        {
            return;
        }
        rebuildQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            rebuildQueued = false;
            Rebuild();
        });
    }

    /// Changes a setting without rebuilding the page (for sliders mid-drag).
    private void Quietly(Action change)
    {
        quiet = true;
        try
        {
            change();
        }
        finally
        {
            quiet = false;
        }
    }

    private void Rebuild()
    {
        theme = Theme.Current();
        Background = theme.Window;
        ((Border)root.Children[0]).Background = theme.Sidebar;
        Foreground = theme.Text;
        BuildSidebar();
        double offset = scroller.VerticalOffset;
        var page = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch };
        TextBlock heading = Controls.Label(theme, section.ToString(), 22, FontWeights.SemiBold);
        heading.Margin = new Thickness(2, 0, 0, 18);
        page.Children.Add(heading);
        foreach (UIElement e in section switch
        {
            SettingsSection.General => GeneralPage(),
            SettingsSection.Pointers => PointersPage(),
            SettingsSection.Appearance => AppearancePage(),
            SettingsSection.Drawing => DrawingPage(),
            SettingsSection.Sound => SoundPage(),
            _ => AboutPage(),
        })
        {
            page.Children.Add(e);
        }
        scroller.Content = page;
        scroller.ScrollToVerticalOffset(offset);
    }

    private void BuildSidebar()
    {
        sidebar.Children.Clear();
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 18) };
        brand.Children.Add(new Image { Source = AppIcon(), Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0) });
        TextBlock name = Controls.Label(theme, "TinyHand", 14, FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(name);
        sidebar.Children.Add(brand);
        foreach (SettingsSection s in Enum.GetValues<SettingsSection>())
        {
            (string glyph, Color color) = s switch
            {
                SettingsSection.General => (Ui.GlyphSettings, Color.FromRgb(140, 140, 140)),
                SettingsSection.Pointers => (Ui.GlyphHand, Color.FromRgb(140, 107, 242)),
                SettingsSection.Appearance => (Ui.GlyphSparkle, Color.FromRgb(242, 115, 153)),
                SettingsSection.Drawing => (Ui.GlyphPen, Color.FromRgb(245, 148, 51)),
                SettingsSection.Sound => (Ui.GlyphVolume, Color.FromRgb(51, 140, 250)),
                _ => (Ui.GlyphInfo, Color.FromRgb(115, 115, 115)),
            };
            var tile = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color),
                Child = Ui.Icon(glyph, 12, Brushes.White),
                Margin = new Thickness(0, 0, 10, 0),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(tile);
            TextBlock label = Controls.Label(theme, s.ToString(), 13, s == section ? FontWeights.SemiBold : FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            var item = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 1, 0, 1),
                Background = s == section ? theme.Selected : Brushes.Transparent,
                Child = row,
                Cursor = Cursors.Hand,
            };
            SettingsSection target = s;
            item.MouseEnter += (_, _) =>
            {
                if (target != section) item.Background = theme.Hover;
            };
            item.MouseLeave += (_, _) =>
            {
                if (target != section) item.Background = Brushes.Transparent;
            };
            item.MouseLeftButtonUp += (_, _) =>
            {
                section = target;
                makingHand = false;
                scroller.ScrollToTop();
                Rebuild();
            };
            sidebar.Children.Add(item);
        }
    }

    // MARK: General

    private IEnumerable<UIElement> GeneralPage()
    {
        yield return Controls.Card(theme, "Shortcuts",
            Controls.Row(theme, "Turn the pointer on or off", null, ShortcutField(Preferences.ToggleShortcut, isToggle: true)),
            Controls.Row(theme, "Clear the drawing", null, ShortcutField(Preferences.ClearShortcut, isToggle: false)));
        if (shortcutError != null)
        {
            TextBlock error = Controls.Label(theme, shortcutError, 12, brush: theme.Danger);
            error.Margin = new Thickness(4, -12, 0, 16);
            yield return error;
        }

        var rows = new List<UIElement>
        {
            Controls.Row(theme, "Hold to use the app", "Clicks, drags and scrolls reach the app.",
                Controls.Segmented(theme, Enum.GetValues<InteractKey>().Select(k => (k.Title(), k)), Preferences.InteractKey,
                    k => Preferences.InteractKey = k)),
        };
        if (Preferences.InteractKey != InteractKey.Off)
        {
            rows.Add(Controls.Row(theme, "While holding",
                Preferences.InteractKeepsHand ? "The hand stays, in menus too." : "The normal cursor comes back, in menus too.",
                Controls.Segmented(theme, new[] { ("Show the cursor", false), ("Keep the hand", true) }, Preferences.InteractKeepsHand,
                    v => Preferences.InteractKeepsHand = v)));
        }
        rows.Add(Controls.Row(theme, "Double-tap Shift to click through",
            "Clicks reach the app and the hand stays. Double-tap again for taps. The tray icon turns orange.",
            Controls.Toggle(theme, Preferences.DoubleShiftClickThrough, v => Preferences.DoubleShiftClickThrough = v)));
        yield return Controls.Card(theme, "Clicking for real", rows.ToArray());

        yield return Controls.Card(theme, "Startup",
            Controls.Row(theme, "Open TinyHand when you sign in", null,
                Controls.Toggle(theme, Preferences.LaunchAtLogin, v => Preferences.LaunchAtLogin = v)));
    }

    private FrameworkElement ShortcutField(Shortcut shortcut, bool isToggle)
    {
        bool recording = isToggle ? recordingToggle : recordingClear;
        FrameworkElement content = recording
            ? Controls.Label(theme, "Type a shortcut…", 12, FontWeights.SemiBold, theme.Accent)
            : Controls.KeyCaps(theme, shortcut.Parts);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        var field = new Border
        {
            Width = 160,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = theme.Control,
            BorderBrush = recording ? theme.Accent : theme.CardBorder,
            BorderThickness = new Thickness(recording ? 2 : 1),
            Child = content,
            Cursor = Cursors.Hand,
            ToolTip = "Click, then press the new shortcut. Esc cancels.",
        };
        field.MouseLeftButtonUp += (_, _) =>
        {
            StopRecording();
            recordingToggle = isToggle;
            recordingClear = !isToggle;
            shortcutError = null;
            OnRecording?.Invoke(true);
            Rebuild();
        };
        return field;
    }

    private void StopRecording()
    {
        if (recordingToggle || recordingClear)
        {
            recordingToggle = false;
            recordingClear = false;
            OnRecording?.Invoke(false);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!recordingToggle && !recordingClear)
        {
            return;
        }
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording();
            Rebuild();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }
        uint mods = 0;
        ModifierKeys m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (Native.KeyDown(Native.VK_LWIN) || Native.KeyDown(Native.VK_RWIN)) mods |= Native.MOD_WIN;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        bool functionKey = vk >= 0x70 && vk <= 0x87;
        if ((mods & (Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_WIN)) == 0 && !functionKey)
        {
            shortcutError = "Use Ctrl, Alt or Win with the key, so it doesn't get in the way of typing.";
            Rebuild();
            return;
        }
        var shortcut = new Shortcut(vk, mods);
        bool isToggle = recordingToggle;
        Shortcut other = isToggle ? Preferences.ClearShortcut : Preferences.ToggleShortcut;
        StopRecording();
        if (shortcut == other)
        {
            shortcutError = $"{shortcut.Display} is already used to {(isToggle ? "clear the drawing" : "turn the pointer on or off")}.";
        }
        else
        {
            bool ok = (isToggle ? ApplyToggleShortcut : ApplyClearShortcut)?.Invoke(shortcut) ?? false;
            shortcutError = ok ? null : $"{shortcut.Display} is taken by another app. Try another.";
        }
        Rebuild();
    }

    // MARK: Pointers

    private IEnumerable<UIElement> PointersPage()
    {
        if (makingHand)
        {
            foreach (UIElement e in MakeHandPage())
            {
                yield return e;
            }
            yield break;
        }

        List<PointerStyle> pinned = Preferences.PinnedStyles;
        var header = new DockPanel { Margin = new Thickness(4, 0, 0, 8), LastChildFill = false };
        header.Children.Add(Controls.Label(theme, $"In your toolbar ({pinned.Count} of {Preferences.MaxPinned})", 12, FontWeights.SemiBold, theme.Secondary));
        TextBlock reset = Controls.TextButton(theme, "Reset", () =>
        {
            pinMessage = null;
            Preferences.PinnedStyles = PointerStyle.DefaultPinned.ToList();
        });
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.Add(reset);
        yield return header;

        var strip = new WrapPanel();
        for (int i = 0; i < pinned.Count; i++)
        {
            strip.Children.Add(PinnedTile(pinned, i));
        }
        yield return new Border
        {
            Background = theme.Card,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = strip,
            Margin = new Thickness(0, 0, 0, 6),
        };
        TextBlock hintText = Controls.Label(theme, pinMessage ?? "Click a pointer below to add or remove it. Use the arrows to reorder.", 11.5,
            brush: pinMessage != null ? theme.Danger : theme.Secondary);
        hintText.Margin = new Thickness(4, 0, 0, 20);
        yield return hintText;

        yield return Section("Hands", PointerStyle.BuiltInHands.Append(PointerStyle.Laser), pinned);

        var customHeader = new DockPanel { Margin = new Thickness(4, 4, 0, 8), LastChildFill = false };
        customHeader.Children.Add(Controls.Label(theme, "Your hands", 12, FontWeights.SemiBold, theme.Secondary));
        TextBlock make = Controls.TextButton(theme, "Make your own…", StartMakingHand);
        DockPanel.SetDock(make, Dock.Right);
        customHeader.Children.Add(make);
        yield return customHeader;
        List<PointerStyle> custom = PointerStyle.CustomHandStyles.ToList();
        if (custom.Count == 0)
        {
            TextBlock empty = Controls.Label(theme, "Turn any idea into a hand with ChatGPT or another image tool.", 12, brush: theme.Secondary);
            empty.Margin = new Thickness(4, 0, 0, 20);
            yield return empty;
        }
        else
        {
            yield return Section(null, custom, pinned);
        }
        if (handMessage != null)
        {
            TextBlock m = Controls.Label(theme, handMessage, 12, brush: theme.Secondary);
            m.Margin = new Thickness(4, -12, 0, 20);
            yield return m;
        }

        yield return Controls.Card(theme, "Toolbar",
            Controls.Row(theme, "Show the toolbar", "It appears when the pointer is near the bottom of the screen.",
                Controls.Toggle(theme, Preferences.ShowToolbar, v => Preferences.ShowToolbar = v)),
            Controls.Row(theme, "Ink colour button", null,
                Controls.Toggle(theme, Preferences.ShowInkButton, v => Preferences.ShowInkButton = v, Preferences.ShowToolbar)));
    }

    private FrameworkElement PinnedTile(List<PointerStyle> pinned, int index)
    {
        PointerStyle style = pinned[index];
        var stack = new StackPanel { Width = 64 };
        stack.Children.Add(new Image { Source = Ui.StyleIcon(style), Width = 40, Height = 40, Margin = new Thickness(0, 2, 0, 2) });
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (index > 0)
        {
            arrows.Children.Add(SmallGlyph("", "Move left", () => Move(index, -1)));
        }
        arrows.Children.Add(SmallGlyph("", "Remove from the toolbar", () => Unpin(style)));
        if (index < pinned.Count - 1)
        {
            arrows.Children.Add(SmallGlyph("", "Move right", () => Move(index, 1)));
        }
        stack.Children.Add(arrows);
        return new Border
        {
            Child = stack,
            Margin = new Thickness(2),
            Padding = new Thickness(2, 4, 2, 2),
            CornerRadius = new CornerRadius(8),
            Background = style == PointerStyle.Saved ? theme.Selected : Brushes.Transparent,
            ToolTip = style.Title,
        };

        void Move(int i, int delta)
        {
            List<PointerStyle> list = Preferences.PinnedStyles;
            int j = i + delta;
            if (j < 0 || j >= list.Count)
            {
                return;
            }
            (list[i], list[j]) = (list[j], list[i]);
            Preferences.PinnedStyles = list;
        }
    }

    private FrameworkElement SmallGlyph(string glyph, string tip, Action action)
    {
        TextBlock g = Ui.Icon(glyph, 9, theme.Secondary);
        var b = new Border { Child = g, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, ToolTip = tip, Background = Brushes.Transparent };
        b.MouseEnter += (_, _) => b.Background = theme.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return b;
    }

    private FrameworkElement Section(string? title, IEnumerable<PointerStyle> styles, List<PointerStyle> pinned)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        if (title != null)
        {
            TextBlock heading = Controls.Label(theme, title, 12, FontWeights.SemiBold, theme.Secondary);
            heading.Margin = new Thickness(4, 0, 0, 8);
            panel.Children.Add(heading);
        }
        var grid = new WrapPanel();
        foreach (PointerStyle style in styles)
        {
            grid.Children.Add(StyleTile(style, pinned.Contains(style)));
        }
        panel.Children.Add(grid);
        return panel;
    }

    private FrameworkElement StyleTile(PointerStyle style, bool isPinned)
    {
        var stack = new StackPanel();
        var image = new Image { Source = Ui.StyleIcon(style), Width = 48, Height = 48, Margin = new Thickness(0, 4, 0, 4) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var imageHost = new Grid();
        imageHost.Children.Add(image);
        if (isPinned)
        {
            var badge = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = theme.Accent,
                Child = Ui.Icon(Ui.GlyphCheck, 9, Brushes.White),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };
            imageHost.Children.Add(badge);
        }
        if (style.IsCustom)
        {
            FrameworkElement delete = SmallGlyph(Ui.GlyphDelete, "Delete this hand", () => RemoveHand(style));
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            delete.VerticalAlignment = VerticalAlignment.Top;
            imageHost.Children.Add(delete);
        }
        stack.Children.Add(imageHost);
        TextBlock label = Controls.Label(theme, style.Title, 11, brush: theme.Secondary);
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextWrapping = TextWrapping.NoWrap;
        stack.Children.Add(label);
        var tile = new Border
        {
            Width = 92,
            Padding = new Thickness(4, 4, 4, 6),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(8),
            Background = theme.Card,
            BorderBrush = isPinned ? theme.Accent : theme.CardBorder,
            BorderThickness = new Thickness(isPinned ? 1.5 : 1),
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = isPinned ? $"{style.Title} — in the toolbar. Click to remove." : $"{style.Title} — click to add to the toolbar.",
        };
        tile.MouseLeftButtonUp += (_, _) => Pin(style);
        var menu = new ContextMenu();
        var use = new MenuItem { Header = "Use this pointer" };
        use.Click += (_, _) =>
        {
            PointerStyle.Saved = style;
            Preferences.Notify();
        };
        menu.Items.Add(use);
        tile.ContextMenu = menu;
        return tile;
    }

    private void Pin(PointerStyle style)
    {
        List<PointerStyle> pinned = Preferences.PinnedStyles;
        if (pinned.Contains(style))
        {
            Unpin(style);
            return;
        }
        if (pinned.Count >= Preferences.MaxPinned)
        {
            pinMessage = $"The toolbar is full. Remove one above to add {style.Title}.";
            Rebuild();
            return;
        }
        pinned.Add(style);
        pinMessage = null;
        Preferences.PinnedStyles = pinned;
    }

    private void Unpin(PointerStyle style)
    {
        List<PointerStyle> pinned = Preferences.PinnedStyles;
        if (!pinned.Contains(style))
        {
            return;
        }
        if (pinned.Count == 1)
        {
            pinMessage = "Keep at least one pointer in the toolbar.";
            Rebuild();
            return;
        }
        pinned.Remove(style);
        pinMessage = null;
        Preferences.PinnedStyles = pinned;
    }

    private void RemoveHand(PointerStyle style)
    {
        if (MessageBox.Show(this, $"Delete {style.Title}?", "TinyHand", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }
        List<PointerStyle> pinned = Preferences.PinnedStyles.Where(s => s != style).ToList();
        Preferences.PinnedStyles = pinned.Count == 0 ? PointerStyle.DefaultPinned.ToList() : pinned;
        if (PointerStyle.Saved == style)
        {
            PointerStyle.Saved = PointerStyle.Lavender;
        }
        CustomHands.Remove(style);
        handMessage = null;
        Preferences.Notify();
    }

    private void StartMakingHand()
    {
        draft = null;
        draftError = null;
        promptCopied = false;
        handMessage = null;
        makingHand = true;
        Rebuild();
    }

    private IEnumerable<UIElement> MakeHandPage()
    {
        TextBlock back = Controls.TextButton(theme, "‹ Pointers", () =>
        {
            makingHand = false;
            Rebuild();
        });
        back.Margin = new Thickness(4, 0, 0, 12);
        yield return back;

        var idea = new TextBox
        {
            Text = handIdea,
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
            Background = theme.Control,
            Foreground = theme.Text,
            BorderBrush = theme.CardBorder,
            CaretBrush = theme.Text,
        };
        idea.TextChanged += (_, _) => handIdea = idea.Text;
        yield return Step(1, "Describe it", "What should your hand look like? For example: knitted wool in mustard yellow.", idea);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Border chat = Controls.Button(theme, "Open in ChatGPT", OpenChatGpt, primary: true, glyph: Ui.GlyphOpen);
        chat.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(chat);
        buttons.Children.Add(Controls.Button(theme, promptCopied ? "Copied" : "Copy prompt", CopyPrompt, glyph: promptCopied ? Ui.GlyphCheck : Ui.GlyphCopy));
        yield return Step(2, "Make the image",
            "Open it in ChatGPT, or copy the prompt for another image tool. Download the result as a PNG with a transparent background.", buttons);

        var add = new StackPanel();
        if (draft is (BitmapSource image, uint ink))
        {
            var preview = new StackPanel { Orientation = Orientation.Horizontal };
            preview.Children.Add(new Border
            {
                Background = theme.Dark ? Ui.Brush(255, 60, 60, 60) : Ui.Brush(255, 236, 236, 236),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Child = new Image { Source = image, Height = 150, Stretch = Stretch.Uniform },
            });
            var form = new StackPanel { Margin = new Thickness(16, 0, 0, 0), Width = 260 };
            form.Children.Add(Controls.Label(theme, "Name", 11, FontWeights.SemiBold, theme.Secondary));
            var nameBox = new TextBox
            {
                Text = handName,
                MaxLength = 16,
                FontSize = 13,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 4, 0, 10),
                Background = theme.Control,
                Foreground = theme.Text,
                BorderBrush = theme.CardBorder,
                CaretBrush = theme.Text,
            };
            nameBox.TextChanged += (_, _) => handName = nameBox.Text;
            form.Children.Add(nameBox);
            var swatch = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            swatch.Children.Add(new Image { Source = Ui.InkDot(Ink.Hex(ink)), Width = 22, Height = 22, Margin = new Thickness(-4, 0, 4, 0) });
            swatch.Children.Add(Controls.Label(theme, "Ink colour, picked from the hand", 11.5, brush: theme.Secondary));
            form.Children.Add(swatch);
            form.Children.Add(Controls.Label(theme, "It should show the back of the hand, like the ones in TinyHand. If you see the palm, try another image.", 11.5, brush: theme.Secondary));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            Border save = Controls.Button(theme, "Add hand", SaveDraft, primary: true);
            save.Margin = new Thickness(0, 0, 12, 0);
            actions.Children.Add(save);
            TextBlock another = Controls.TextButton(theme, "Use another image", ChooseHandFile);
            another.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(another);
            form.Children.Add(actions);
            preview.Children.Add(form);
            add.Children.Add(preview);
        }
        else
        {
            add.Children.Add(Controls.Button(theme, draftError == null ? "Choose the PNG…" : "Try again with another image", ChooseHandFile, glyph: Ui.GlyphAdd));
            if (draftError != null)
            {
                TextBlock error = Controls.Label(theme, draftError, 12, brush: theme.Danger);
                error.Margin = new Thickness(0, 8, 0, 0);
                add.Children.Add(error);
            }
        }
        yield return Step(3, "Add it", null, add);
    }

    private FrameworkElement Step(int number, string title, string? detail, UIElement body)
    {
        var grid = new Grid { Margin = new Thickness(16, 14, 16, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var circle = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = theme.Accent,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = number.ToString(),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(circle);
        var right = new StackPanel();
        right.Children.Add(Controls.Label(theme, title, 14, FontWeights.SemiBold));
        if (detail != null)
        {
            TextBlock d = Controls.Label(theme, detail, 12, brush: theme.Secondary);
            d.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(d);
        }
        if (body is FrameworkElement fe)
        {
            fe.Margin = new Thickness(0, 10, 0, 0);
        }
        right.Children.Add(body);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border
        {
            Background = theme.Card,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 12),
            Child = grid,
        };
    }

    private void OpenChatGpt()
    {
        string url = "https://chatgpt.com/?q=" + Uri.EscapeDataString(HandMaker.Prompt(handIdea));
        Open(url);
    }

    private void CopyPrompt()
    {
        try
        {
            Clipboard.SetText(HandMaker.Prompt(handIdea));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }
        promptCopied = true;
        Rebuild();
        var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        reset.Tick += (_, _) =>
        {
            reset.Stop();
            promptCopied = false;
            if (makingHand)
            {
                Rebuild();
            }
        };
        reset.Start();
    }

    private void ChooseHandFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick the PNG of your hand",
            Filter = "Images (*.png;*.webp)|*.png;*.webp|All files (*.*)|*.*",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        LoadDraft(dialog.FileName);
    }

    private void LoadDraft(string path)
    {
        try
        {
            draft = HandMaker.Make(path);
            draftError = null;
            handName = CustomHands.SuggestedName(path);
        }
        catch (HandMaker.Problem p)
        {
            draft = null;
            draftError = p.Message;
        }
        catch (Exception e)
        {
            Log.Write($"hand load failed: {e}");
            draft = null;
            draftError = "TinyHand couldn't read that file.";
        }
        Rebuild();
    }

    private void SaveDraft()
    {
        if (draft is not (BitmapSource image, uint ink))
        {
            return;
        }
        try
        {
            PointerStyle style = CustomHands.Add(image, ink, handName);
            if (style.AssetName is string name)
            {
                HandAsset.Thumbnail(name);
            }
            List<PointerStyle> pinned = Preferences.PinnedStyles;
            if (pinned.Count < Preferences.MaxPinned)
            {
                pinned.Insert(Math.Max(0, pinned.Count - (pinned.LastOrDefault() == PointerStyle.Laser ? 1 : 0)), style);
                Preferences.PinnedStyles = pinned;
                handMessage = $"{style.Title} is in your toolbar.";
            }
            else
            {
                handMessage = $"{style.Title} is ready. Click it to add it to the toolbar.";
            }
            PointerStyle.Saved = style;
            draft = null;
            handIdea = "";
            makingHand = false;
            Preferences.Notify();
        }
        catch (Exception e)
        {
            Log.Write($"hand save failed: {e}");
            draftError = "TinyHand couldn't save that hand.";
            Rebuild();
        }
    }

    // MARK: Appearance

    private IEnumerable<UIElement> AppearancePage()
    {
        var slider = new Slider
        {
            Minimum = Preferences.PointerSizeMin,
            Maximum = Preferences.PointerSizeMax,
            Value = Preferences.PointerSize,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center,
            IsMoveToPointEnabled = true,
        };
        slider.ValueChanged += (_, e) => Quietly(() => Preferences.PointerSize = e.NewValue);
        var size = new StackPanel { Orientation = Orientation.Horizontal };
        size.Children.Add(Ui.Icon(Ui.GlyphHand, 10, theme.Secondary));
        slider.Margin = new Thickness(8, 0, 8, 0);
        size.Children.Add(slider);
        size.Children.Add(Ui.Icon(Ui.GlyphHand, 17, theme.Secondary));
        yield return Controls.Card(theme, "Pointer",
            Controls.Row(theme, "Size", null, size),
            Controls.Row(theme, "Shadow under the pointer", null, Controls.Toggle(theme, Preferences.ShowShadow, v => Preferences.ShowShadow = v)));
    }

    // MARK: Drawing

    private IEnumerable<UIElement> DrawingPage()
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (InkChoice choice in Enum.GetValues<InkChoice>())
        {
            bool selected = Preferences.InkChoice == choice;
            var b = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                BorderBrush = selected ? theme.Accent : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(1, 0, 1, 0),
                Child = new Image { Source = choice.Color() is Color c ? Ui.InkDot(c) : Ui.RainbowDot() },
                Cursor = Cursors.Hand,
                ToolTip = choice.Title(),
            };
            b.MouseLeftButtonUp += (_, _) => Preferences.InkChoice = choice;
            swatches.Children.Add(b);
        }
        string inkDetail = Preferences.InkChoice == InkChoice.Match
            ? "Matches each pointer."
            : $"All pointers draw in {Preferences.InkChoice.Title().ToLowerInvariant()}.";
        double fade = Preferences.FadeSeconds;
        double current = FadeChoices.Any(c => c.seconds == fade) ? fade : 3;
        yield return Controls.Card(theme, "Ink",
            Controls.Row(theme, "Ink colour", inkDetail, swatches),
            Controls.Row(theme, "Lines fade", $"Clear them any time with {Preferences.ClearShortcut.Display}.",
                Controls.Segmented(theme, FadeChoices.Select(c => (c.title, c.seconds)), current, v => Preferences.FadeSeconds = v)),
            Controls.Row(theme, "Arrows", "Hold Ctrl while drawing to add an arrowhead to the end of the line.", new FrameworkElement()));
    }

    // MARK: Sound

    private IEnumerable<UIElement> SoundPage()
    {
        previewSound ??= new TapSound();
        previewSound.Warm();
        bool on = Preferences.HandSound;
        TextBlock heading = Controls.Label(theme, "Tap sound", 12, FontWeights.SemiBold, theme.Secondary);
        heading.Margin = new Thickness(4, 0, 0, 8);
        yield return heading;
        var grid = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        grid.Children.Add(SoundTile("Off", Ui.GlyphMute, !on, () => Preferences.HandSound = false));
        foreach (TapSoundChoice choice in Enum.GetValues<TapSoundChoice>())
        {
            grid.Children.Add(SoundTile(choice.Title(), Ui.GlyphWave, on && Preferences.TapSound == choice, () =>
            {
                Preferences.TapSound = choice;
                Preferences.HandSound = true;
                previewSound.Load(choice);
                previewSound.PlayAlways();
            }));
        }
        yield return grid;
        TextBlock note = Controls.Label(theme, "Click a sound to hear it. It plays each time the hand taps.", 11.5, brush: theme.Secondary);
        note.Margin = new Thickness(4, 0, 0, 20);
        yield return note;
        FrameworkElement card = Controls.Card(theme, null,
            Controls.Row(theme, "Laser dot taps too", "Off by default, for quiet pointing.",
                Controls.Toggle(theme, Preferences.LaserSound, v => Preferences.LaserSound = v, on)));
        card.Opacity = on ? 1 : 0.5;
        yield return card;
    }

    private FrameworkElement SoundTile(string title, string glyph, bool selected, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock icon = Ui.Icon(glyph, 13, selected ? theme.Accent : theme.Secondary);
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        row.Children.Add(Controls.Label(theme, title, 12.5, selected ? FontWeights.SemiBold : FontWeights.Normal));
        var tile = new Border
        {
            Width = 112,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(8),
            Background = theme.Card,
            BorderBrush = selected ? theme.Accent : theme.CardBorder,
            BorderThickness = new Thickness(selected ? 1.5 : 1),
            Child = row,
            Cursor = Cursors.Hand,
        };
        tile.MouseLeftButtonUp += (_, _) => action();
        return tile;
    }

    // MARK: About

    private IEnumerable<UIElement> AboutPage()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version is Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0";
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        top.Children.Add(new Image { Source = AppIcon(), Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Controls.Label(theme, "TinyHand for Windows", 18, FontWeights.Bold));
        text.Children.Add(Controls.Label(theme, $"Version {version}", 12, brush: theme.Secondary));
        text.Children.Add(Controls.Label(theme, "A tiny hand for your screen. Free and open source.", 12, brush: theme.Secondary));
        top.Children.Add(text);
        yield return Controls.Card(theme, null, top);

        yield return Controls.Card(theme, null,
            LinkRow("Original app (macOS)", "github.com/nilbuild/tinyhand", "https://github.com/nilbuild/tinyhand"),
            LinkRow("Website", "tinyhand.app", "https://tinyhand.app"),
            LinkRow("License", "MIT", "https://github.com/nilbuild/tinyhand/blob/main/LICENSE"),
            LinkRow("Log file", "Open", Log.Path));

        TextBlock reset = Controls.TextButton(theme, "Reset all settings…", ResetAll, theme.Danger);
        reset.Margin = new Thickness(4, 0, 0, 0);
        yield return reset;
    }

    private FrameworkElement LinkRow(string title, string detail, string target)
    {
        TextBlock link = Controls.TextButton(theme, detail, () => Open(target));
        return Controls.Row(theme, title, null, link);
    }

    private void ResetAll()
    {
        if (MessageBox.Show(this,
                "Shortcuts, toolbar pointers, size, shadow, drawing and sound go back to how they were at first. Open at sign-in stays as it is.",
                "Reset all settings?", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        Preferences.ResetAll();
        ApplyToggleShortcut?.Invoke(Shortcut.ToggleDefault);
        ApplyClearShortcut?.Invoke(Shortcut.ClearDefault);
        shortcutError = null;
        pinMessage = null;
        Rebuild();
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write($"could not open {target}: {e.Message}");
        }
    }
}
