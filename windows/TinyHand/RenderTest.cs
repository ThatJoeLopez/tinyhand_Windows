using System;
using System.IO;
using System.Linq;
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

        var names = PointerStyle.AllCases.Select(s => s.AssetName).OfType<string>().ToList();

        // TinyHand's own PNG reader must give the same pixels as the system decoder.
        foreach (string name in names)
        {
            string? path = Paths.Asset(name + ".png");
            BitmapSource? own = path == null ? null : PngCleaner.TryDecode(path);
            if (path == null || own == null)
            {
                Console.WriteLine($"own decoder skipped {name}");
                continue;
            }
            using var stream = File.OpenRead(path);
            BitmapSource system = new FormatConvertedBitmap(
                BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0],
                PixelFormats.Bgra32, null, 0);
            var a = new byte[own.PixelWidth * own.PixelHeight * 4];
            var b = new byte[a.Length];
            own.CopyPixels(a, own.PixelWidth * 4, 0);
            system.CopyPixels(b, system.PixelWidth * 4, 0);
            int diff = 0;
            for (int k = 0; k < a.Length; k++)
            {
                diff = Math.Max(diff, Math.Abs(a[k] - b[k]));
            }
            if (diff > 0)
            {
                Console.WriteLine($"decoders differ on {name}: max {diff}");
            }
        }
        Console.WriteLine("compared decoders");

        // Thumbnails, made the way the app makes them: in the background at launch.
        var loaded = new DispatcherFrame();
        HandAsset.PreloadThumbnails(names, () => loaded.Continue = false);
        Dispatcher.PushFrame(loaded);
        var grid = new System.Windows.Controls.WrapPanel { Width = 9 * 72, Background = Brushes.White };
        int missing = 0;
        foreach (string name in names)
        {
            BitmapSource? thumb = HandAsset.Thumbnail(name);
            if (thumb == null || IsBlank(thumb))
            {
                missing++;
                Console.WriteLine($"blank thumbnail: {name}");
            }
            grid.Children.Add(new System.Windows.Controls.Image { Source = thumb, Width = 64, Height = 64, Margin = new Thickness(4) });
        }
        Save(grid, 9 * 72, (names.Count + 8) / 9 * 72, Path.Combine(folder, "thumbnails.png"));
        Console.WriteLine($"rendered thumbnails, {names.Count - missing} of {names.Count} drawn");

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

    private static bool IsBlank(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, image.PixelWidth * 4, 0);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0)
            {
                return false;
            }
        }
        return true;
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
