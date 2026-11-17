using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TinyHand;

/// `TinyHand.exe --render-test <folder>` draws the hand, laser, toolbar and each Settings page
/// to PNG files, so the drawing can be checked without a real screen.
internal static class RenderTest
{
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);

        var arm = new ArmView();
        arm.SetScreenSize(1600, 900);
        foreach ((string name, Point cursor) in new[] { ("arm-center", new Point(800, 450)), ("arm-topleft", new Point(150, 120)), ("arm-right", new Point(1450, 300)) })
        {
            for (int k = 0; k < 120; k++)
            {
                arm.Aim(cursor);
            }
            var canvas = new System.Windows.Controls.Grid { Width = 1600, Height = 900, Background = new SolidColorBrush(Color.FromRgb(236, 238, 242)) };
            canvas.Children.Add(arm);
            Save(canvas, 1600, 900, Path.Combine(folder, name + ".png"));
            canvas.Children.Remove(arm);
            Console.WriteLine($"rendered {name}");
        }

        var toolbar = new Toolbar();
        toolbar.Select(PointerStyle.Saved);

        foreach (SettingsSection section in Enum.GetValues<SettingsSection>())
        {
            var settings = new SettingsWindow();
            settings.ShowSection(section);
            Pump();
            if (settings.Content is FrameworkElement content)
            {
                Save(content, (int)Math.Max(1, content.ActualWidth), (int)Math.Max(1, content.ActualHeight),
                    Path.Combine(folder, $"settings-{section.ToString().ToLowerInvariant()}.png"));
            }
            settings.Close();
            Console.WriteLine($"rendered settings {section}");
        }

        var welcome = new WelcomeWindow();
        welcome.Show();
        Pump();
        if (welcome.Content is FrameworkElement card)
        {
            Save(card, (int)card.ActualWidth, (int)card.ActualHeight, Path.Combine(folder, "welcome.png"));
        }
        welcome.Close();
        Console.WriteLine("done");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void Save(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
