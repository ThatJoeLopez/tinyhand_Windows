using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TinyHand;

/// Draws the hand on its stick (or the laser dot) and the ink. Coordinates are DIPs, y down.
///
/// The stick is held from below the bottom edge of the screen. Its grip leans to the right of
/// the cursor and drifts after it, and the stick turns so the fingertip lands on the cursor.
internal sealed class ArmView : Canvas
{
    private readonly Canvas shadowHost = new() { IsHitTestVisible = false };
    private readonly Canvas group = new() { IsHitTestVisible = false };
    private readonly Image rod = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Image hand = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly RotateTransform rotate = new();
    private readonly DropShadowEffect shadow = new()
    {
        Color = Colors.Black,
        RenderingBias = RenderingBias.Performance,
    };

    private readonly Canvas dot = new() { IsHitTestVisible = false };
    private readonly Ellipse dotGlow = new() { IsHitTestVisible = false };
    private readonly Ellipse dotBody = new() { IsHitTestVisible = false };
    private readonly Ellipse dotCore = new() { IsHitTestVisible = false };

    public PointerStyle Pointer { get; private set; } = PointerStyle.Lavender;
    private HandAsset? asset;

    private double handLenFrac = Preferences.PointerSize;
    private bool showShadow = Preferences.ShowShadow;
    private const double MinRodFrac = 0.05;
    private const double GripLeanFrac = 0.30;
    private const double GripDepthFrac = 0.12;
    private const double GripFollow = 0.08;
    private const double LiftFrac = 0.06;

    public Ink Ink { get; } = new();
    public bool Held { get; set; }

    private double pressAmount;
    private double? gripX;
    private double? gripTarget;
    private double[] lastPose = Array.Empty<double>();
    private double[] lastLaserPose = Array.Empty<double>();
    private int displayImageHeight;
    private bool pointerHidden;

    public double ScreenWidth { get; private set; } = 1920;
    public double ScreenHeight { get; private set; } = 1080;

    /// Device pixels per DIP on the current monitor, used to pick an image resolution.
    public double Scale { get; set; } = 1;

    public double InkWidth => Math.Max(3, ScreenHeight * 0.0055);

    public bool IsSettling
    {
        get
        {
            if (pressAmount > 0.002)
            {
                return true;
            }
            return gripX is double gx && gripTarget is double target && Math.Abs(target - gx) > 0.5;
        }
    }

    public bool PointerHidden
    {
        get => pointerHidden;
        set
        {
            pointerHidden = value;
            shadowHost.Visibility = value || Pointer.IsLaser ? Visibility.Hidden : Visibility.Visible;
            dot.Visibility = value || !Pointer.IsLaser ? Visibility.Hidden : Visibility.Visible;
        }
    }

    public ArmView()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Children.Add(Ink.Layer);
        Children.Add(shadowHost);
        Children.Add(dot);

        shadowHost.Children.Add(group);
        shadowHost.Effect = shadow;
        group.Children.Add(rod);
        group.Children.Add(hand);
        group.RenderTransform = rotate;
        RenderOptions.SetBitmapScalingMode(rod, BitmapScalingMode.Linear);
        RenderOptions.SetBitmapScalingMode(hand, BitmapScalingMode.Linear);

        var red = Ink.Red;
        dotGlow.Fill = new RadialGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(150, red.R, red.G, red.B), 0.0),
            new GradientStop(Color.FromArgb(70, red.R, red.G, red.B), 0.45),
            new GradientStop(Color.FromArgb(0, red.R, red.G, red.B), 1.0),
        });
        dotBody.Fill = new SolidColorBrush(red);
        dotCore.Fill = new SolidColorBrush(Color.FromArgb(242, 255, 219, 209));
        dot.Children.Add(dotGlow);
        dot.Children.Add(dotBody);
        dot.Children.Add(dotCore);

        SetStyle(PointerStyle.Saved);
        Preferences.Changed += () =>
        {
            handLenFrac = Preferences.PointerSize;
            showShadow = Preferences.ShowShadow;
            lastPose = Array.Empty<double>();
            lastLaserPose = Array.Empty<double>();
        };
    }

    public void SetScreenSize(double width, double height)
    {
        ScreenWidth = width;
        ScreenHeight = height;
        Width = width;
        Height = height;
        gripX = null;
        lastPose = Array.Empty<double>();
        lastLaserPose = Array.Empty<double>();
        displayImageHeight = 0;
    }

    public void UpdateInkColor() => Ink.Color = Preferences.InkChoice.Resolved(Pointer);

    public void SetStyle(PointerStyle newStyle)
    {
        Pointer = newStyle;
        asset = newStyle.AssetName is { } name ? HandAsset.Load(name) : null;
        if (!newStyle.IsLaser && asset == null)
        {
            Log.Write($"hand image for {newStyle.Id} is missing; using the laser dot");
            Pointer = PointerStyle.Laser;
        }
        UpdateInkColor();
        displayImageHeight = 0;
        lastPose = Array.Empty<double>();
        lastLaserPose = Array.Empty<double>();
        if (asset != null && !Pointer.IsLaser)
        {
            UpdateDisplayImage(asset);
        }
        PointerHidden = pointerHidden;
    }

    public void Press() => pressAmount = 1;

    /// Points the fingertip (or the dot) at the cursor, given in overlay DIPs.
    public void Aim(Point cursor)
    {
        double screenH = ScreenHeight;
        double screenW = ScreenWidth;
        double targetX = Math.Min(Math.Max(cursor.X + GripLeanFrac * screenW, screenW * 0.15), screenW * 1.05);
        double gx = gripX is double old ? old + (targetX - old) * GripFollow : targetX;
        gripX = gx;
        gripTarget = targetX;
        var grip = new Point(gx, screenH + GripDepthFrac * screenH);

        if (!Held)
        {
            pressAmount += (0 - pressAmount) * 0.18;
        }

        if (Pointer.IsLaser)
        {
            AimLaser(cursor);
            return;
        }
        if (asset is not HandAsset a)
        {
            return;
        }
        UpdateDisplayImage(a);

        // Work in y-up space (as on the Mac) and flip the final angle.
        double dx = cursor.X - grip.X;
        double dy = grip.Y - cursor.Y;
        double reach = Math.Sqrt(dx * dx + dy * dy);
        double aimAngle = Math.Atan2(dy, dx);

        double handPts = handLenFrac * screenH * (1 - 0.06 * pressAmount);
        double s = handPts / (a.Wrist - a.HandTop);
        double widthPts = s * a.Aspect;
        double handTipPts = (a.Wrist - a.Tip) * s;
        double tipOffX = (a.TipX - a.RodX) * widthPts;

        double along = Math.Sqrt(Math.Max(0, reach * reach - tipOffX * tipOffX));
        double rodLen = Math.Max(MinRodFrac * screenH, along - handTipPts);
        double tipAngle = Math.Atan2(rodLen + handTipPts, tipOffX);
        double rotationUp = aimAngle - tipAngle;

        double lift = LiftFrac * screenH * (1 - 0.8 * pressAmount);

        double[] pose = new[] { rotationUp, rodLen, handPts, widthPts, grip.X, lift, pressAmount, showShadow ? 1 : 0, a.Aspect, screenH }
            .Select(v => Math.Round(v * 100)).ToArray();
        if (pose.SequenceEqual(lastPose))
        {
            return;
        }
        lastPose = pose;

        // The shadow falls down and to the right in screen space, further while the hand is lifted.
        double sx = lift * 0.6;
        double sy = lift;
        shadow.ShadowDepth = Math.Sqrt(sx * sx + sy * sy);
        shadow.Direction = Math.Atan2(-sy, sx) * 180 / Math.PI;
        shadow.BlurRadius = (Math.Max(1, lift * 0.35) + 5 * pressAmount) * 2;
        shadow.Opacity = showShadow ? 0.26 + 0.02 * pressAmount : 0;

        double total = rodLen + handPts;
        group.Width = widthPts;
        group.Height = total;
        hand.Width = widthPts;
        hand.Height = handPts;
        Canvas.SetLeft(hand, 0);
        Canvas.SetTop(hand, 0);
        rod.Width = widthPts;
        rod.Height = rodLen;
        Canvas.SetLeft(rod, 0);
        Canvas.SetTop(rod, handPts);

        // The grip sits at the bottom of the rod, at the rod's centre line.
        double originX = a.RodX * widthPts;
        Canvas.SetLeft(group, grip.X - originX);
        Canvas.SetTop(group, grip.Y - total);
        rotate.CenterX = originX;
        rotate.CenterY = total;
        // WPF angles turn clockwise on screen; the y-up rotation turns counter-clockwise.
        rotate.Angle = -rotationUp * 180 / Math.PI;
    }

    private void AimLaser(Point cursor)
    {
        double press = pressAmount;
        double r = Math.Max(4, ScreenHeight * 0.0038 * handLenFrac / 0.13) * (1 + 0.2 * press);
        double[] pose = new[] { r, cursor.X, cursor.Y }.Select(v => Math.Round(v * 100)).ToArray();
        if (pose.SequenceEqual(lastLaserPose))
        {
            return;
        }
        lastLaserPose = pose;
        double core = r * 0.45;
        double glow = r * (3.4 + 1.2 * press);
        Place(dotGlow, glow);
        Place(dotBody, r);
        Place(dotCore, core);
        Canvas.SetLeft(dot, cursor.X);
        Canvas.SetTop(dot, cursor.Y);
    }

    private static void Place(Ellipse e, double radius)
    {
        e.Width = radius * 2;
        e.Height = radius * 2;
        Canvas.SetLeft(e, -radius);
        Canvas.SetTop(e, -radius);
    }

    /// Picks a copy of the hand image just big enough for the current size, in 64 px steps.
    private void UpdateDisplayImage(HandAsset a)
    {
        BitmapSource full = a.Image;
        double restPts = handLenFrac * ScreenHeight / (a.Wrist - a.HandTop);
        const int step = 64;
        int needed = ((int)Math.Ceiling(restPts * Scale) + step - 1) / step * step;
        int height = needed >= full.PixelHeight * 9 / 10 ? full.PixelHeight : Math.Max(step, needed);
        if (height == displayImageHeight)
        {
            return;
        }
        displayImageHeight = height;
        BitmapSource image = height == full.PixelHeight ? full : HandAsset.Resized(full, height);
        int h = image.PixelHeight;
        int w = image.PixelWidth;
        int handY = Math.Clamp((int)Math.Round(a.HandTop * h), 0, h - 1);
        int wristY = Math.Clamp((int)Math.Round(a.Wrist * h), handY + 1, h);
        int rodY = Math.Clamp((int)Math.Round(a.RodEnd * h), wristY + 1, h);
        hand.Source = Freeze(new CroppedBitmap(image, new Int32Rect(0, handY, w, wristY - handY)));
        rod.Source = Freeze(new CroppedBitmap(image, new Int32Rect(0, wristY, w, Math.Max(1, rodY - wristY))));
    }

    private static BitmapSource Freeze(BitmapSource b)
    {
        b.Freeze();
        return b;
    }
}
