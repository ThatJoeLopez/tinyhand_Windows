using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TinyHand;

/// A borderless, transparent, always-on-top window that never takes focus.
internal class FloatingWindow : Window
{
    public IntPtr Handle { get; private set; }
    private bool clickThrough;

    public FloatingWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        Native.SetExStyle(Handle, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW, Native.WS_EX_APPWINDOW);
        if (clickThrough)
        {
            Native.SetExStyle(Handle, Native.WS_EX_TRANSPARENT);
        }
        HwndSource.FromHwnd(Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        });
    }

    /// Lets mouse events fall through to whatever is underneath.
    public bool ClickThrough
    {
        get => clickThrough;
        set
        {
            clickThrough = value;
            if (Handle != IntPtr.Zero)
            {
                if (value)
                {
                    Native.SetExStyle(Handle, Native.WS_EX_TRANSPARENT);
                }
                else
                {
                    Native.SetExStyle(Handle, 0, Native.WS_EX_TRANSPARENT);
                }
            }
        }
    }

    /// Shows the window without activating it.
    public void ShowQuietly()
    {
        if (!IsVisible)
        {
            Show();
        }
        RaiseToTop();
    }

    public void RaiseToTop()
    {
        if (Handle != IntPtr.Zero)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
    }

    /// Window bounds in physical pixels.
    public Native.RECT PixelBounds
    {
        get
        {
            if (Handle == IntPtr.Zero || !IsVisible)
            {
                return default;
            }
            Native.GetWindowRect(Handle, out Native.RECT r);
            return r;
        }
    }

    /// Moves the window's top-left corner to a point in physical pixels.
    public void MoveToPixels(int x, int y)
    {
        if (Handle != IntPtr.Zero)
        {
            Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
    }

    public void FadeTo(double opacity, double seconds, Action? done = null)
    {
        var anim = new DoubleAnimation(opacity, TimeSpan.FromSeconds(seconds));
        if (done != null)
        {
            anim.Completed += (_, _) => done();
        }
        BeginAnimation(OpacityProperty, anim);
    }

    public double DpiScale => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
}

internal static class Ui
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string GlyphVolume = "";
    public const string GlyphMute = "";
    public const string GlyphClose = "";
    public const string GlyphSettings = "";
    public const string GlyphHand = "";
    public const string GlyphSparkle = "";
    public const string GlyphPen = "";
    public const string GlyphInfo = "";
    public const string GlyphCheck = "";
    public const string GlyphCopy = "";
    public const string GlyphOpen = "";
    public const string GlyphAdd = "";
    public const string GlyphWave = "";
    public const string GlyphDelete = "";
    public const string GlyphPin = "";

    public static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush White(double alpha) => Brush((byte)(alpha * 255), 255, 255, 255);

    /// The dark rounded panel the toolbar, tray and hint sit on.
    public static Border DarkGlass(double radius, UIElement child, Thickness padding) => new()
    {
        CornerRadius = new CornerRadius(radius),
        Background = Brush(232, 24, 24, 27),
        BorderBrush = White(0.12),
        BorderThickness = new Thickness(1),
        Padding = padding,
        Child = child,
        Margin = new Thickness(10),
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 3,
            Direction = 270,
            Opacity = 0.35,
            Color = Colors.Black,
        },
    };

    public static TextBlock Icon(string glyph, double size, Brush foreground) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        Foreground = foreground,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static ImageSource InkDot(Color color)
    {
        var group = new DrawingGroup();
        var c = new Point(32, 32);
        const double r = 13;
        group.Children.Add(new GeometryDrawing(Brush(89, 0, 0, 0), null, new EllipseGeometry(new Point(c.X, c.Y + 1), r + 1.5, r + 1.5)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new EllipseGeometry(c, r, r)));
        group.Children.Add(new GeometryDrawing(White(0.35), null, new EllipseGeometry(new Point(c.X - r * 0.2, c.Y - r * 0.4), r * 0.35, r * 0.25)));
        return Frame(group);
    }

    public static ImageSource RainbowDot()
    {
        var group = new DrawingGroup();
        var c = new Point(32, 32);
        const double r = 13;
        group.Children.Add(new GeometryDrawing(Brush(89, 0, 0, 0), null, new EllipseGeometry(new Point(c.X, c.Y + 1), r + 1.5, r + 1.5)));
        Color[] colors =
        {
            Color.FromRgb(255, 59, 48), Color.FromRgb(255, 149, 0), Color.FromRgb(255, 204, 0),
            Color.FromRgb(52, 199, 89), Color.FromRgb(0, 122, 255), Color.FromRgb(175, 82, 222),
        };
        for (int i = 0; i < colors.Length; i++)
        {
            double a0 = (i * 60 - 90) * Math.PI / 180;
            double a1 = ((i + 1) * 60 - 90) * Math.PI / 180;
            var figure = new PathFigure { StartPoint = c, IsClosed = true };
            figure.Segments.Add(new LineSegment(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false));
            figure.Segments.Add(new ArcSegment(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, false, SweepDirection.Clockwise, false));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[i]), null, new PathGeometry(new[] { figure })));
        }
        return Frame(group);
    }

    public static ImageSource LaserIcon()
    {
        var group = new DrawingGroup();
        var c = new Point(32, 32);
        Color red = Ink.Red;
        var glow = new RadialGradientBrush(Color.FromArgb(140, red.R, red.G, red.B), Color.FromArgb(0, red.R, red.G, red.B));
        group.Children.Add(new GeometryDrawing(glow, null, new EllipseGeometry(c, 18, 18)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(red), null, new EllipseGeometry(c, 7, 7)));
        group.Children.Add(new GeometryDrawing(Brush(242, 255, 219, 209), null, new EllipseGeometry(c, 3, 3)));
        return Frame(group);
    }

    /// Pins a drawing to a 64×64 box so icons line up.
    private static ImageSource Frame(DrawingGroup group)
    {
        group.Children.Insert(0, new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    public static ImageSource? StyleIcon(PointerStyle style) =>
        style.AssetName is { } name ? HandAsset.Thumbnail(name) : LaserIcon();
}
