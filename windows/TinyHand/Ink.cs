using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace TinyHand;

/// Strokes drawn by dragging. Coordinates are DIPs in the overlay, y pointing down.
internal sealed class Ink
{
    public static readonly Color Purple = Color.FromRgb(135, 84, 242);
    public static readonly Color Red = Color.FromRgb(255, 41, 31);

    public static Color Hex(uint v) => Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));

    public Canvas Layer { get; } = new() { IsHitTestVisible = false };

    public Color Color { get; set; } = Purple;

    private static bool Keep => Preferences.FadeSeconds <= 0;
    private static double FadeDelay => Math.Max(0.5, Preferences.FadeSeconds);
    private const double FadeDuration = 0.6;

    private Path? stroke;
    private readonly List<Point> points = new();
    private bool arrow;

    public bool IsDrawing => stroke != null;

    public int StrokeCount => Layer.Children.Count;

    public bool Arrow
    {
        get => arrow;
        set
        {
            if (arrow != value)
            {
                arrow = value;
                Redraw();
            }
        }
    }

    public void Begin(Point point, double width)
    {
        var brush = new SolidColorBrush(Color);
        brush.Freeze();
        var s = new Path
        {
            Stroke = brush,
            StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.18,
                BlurRadius = 4,
                ShadowDepth = 1.8,
                Direction = 304,
                RenderingBias = RenderingBias.Performance,
            },
        };
        Layer.Children.Add(s);
        stroke = s;
        points.Clear();
        points.Add(point);
        Redraw();
    }

    public void Extend(Point point)
    {
        if (stroke == null || points.Count == 0)
        {
            return;
        }
        Point last = points[^1];
        if ((point - last).Length < 1.5)
        {
            return;
        }
        points.Add(point);
        Redraw();
    }

    public void End()
    {
        Path? s = stroke;
        if (s == null)
        {
            return;
        }
        stroke = null;
        points.Clear();
        arrow = false;
        // A finished stroke never changes again: cache it as a bitmap so the effect isn't recomputed.
        s.CacheMode = new BitmapCache { RenderAtScale = 1, SnapsToDevicePixels = false };
        if (Keep)
        {
            return;
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(FadeDuration))
        {
            BeginTime = TimeSpan.FromSeconds(FadeDelay),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) => Layer.Children.Remove(s);
        s.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public void Clear()
    {
        stroke = null;
        points.Clear();
        foreach (UIElement child in Layer.Children.OfType<UIElement>().ToList())
        {
            child.BeginAnimation(UIElement.OpacityProperty, null);
        }
        Layer.Children.Clear();
    }

    private void Redraw()
    {
        if (stroke == null || points.Count == 0)
        {
            return;
        }
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            Point first = points[0];
            ctx.BeginFigure(first, false, false);
            if (points.Count == 1)
            {
                ctx.LineTo(first, true, true);
            }
            for (int i = 1; i < points.Count; i++)
            {
                Point p = points[i];
                Point prev = points[i - 1];
                var mid = new Point((prev.X + p.X) / 2, (prev.Y + p.Y) / 2);
                ctx.QuadraticBezierTo(prev, mid, true, true);
            }
            if (points.Count > 1)
            {
                ctx.LineTo(points[^1], true, true);
            }
            if (arrow)
            {
                AddHead(ctx, Math.Max(14, stroke.StrokeThickness * 3.2));
            }
        }
        geometry.Freeze();
        stroke.Data = geometry;
    }

    // Aims the head along the last stretch of the stroke, so a wobble at the very end doesn't twist it.
    private void AddHead(StreamGeometryContext ctx, double size)
    {
        Point end = points[^1];
        Point back = points.LastOrDefault(p => (end - p).Length >= size * 1.2, points[0]);
        double length = (end - back).Length;
        if (length <= 1)
        {
            return;
        }
        double head = Math.Min(size, length * 0.8);
        double angle = Math.Atan2(end.Y - back.Y, end.X - back.X);
        const double spread = Math.PI / 7;
        foreach (int side in new[] { -1, 1 })
        {
            double a = angle + Math.PI + side * spread;
            ctx.BeginFigure(end, false, false);
            ctx.LineTo(new Point(end.X + Math.Cos(a) * head, end.Y + Math.Sin(a) * head), true, true);
        }
    }
}
