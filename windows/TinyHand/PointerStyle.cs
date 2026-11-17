using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TinyHand;

internal sealed class PointerStyle : IEquatable<PointerStyle>
{
    public string Id { get; }

    private PointerStyle(string id)
    {
        Id = id;
    }

    public static readonly PointerStyle Lavender = new("lavender");
    public static readonly PointerStyle Glove = new("glove");
    public static readonly PointerStyle Coral = new("coral");
    public static readonly PointerStyle Plush = new("plush");
    public static readonly PointerStyle Robot = new("robot");
    public static readonly PointerStyle Laser = new("laser");

    private static readonly (string id, string title, Color ink)[] BuiltIn =
    {
        ("lavender", "Lavender toy", Ink.Purple),
        ("glove", "Cursor glove", Ink.Purple),
        ("coral", "Coral vinyl", Ink.Red),
        ("plush", "Plush", Ink.Hex(0x2F86F0)),
        ("robot", "Robot", Ink.Hex(0x16A87A)),
        ("pink", "Bubblegum", Ink.Hex(0xEE4F98)),
        ("blue", "Sky", Ink.Hex(0x2F86F0)),
        ("mint", "Mint", Ink.Hex(0x16A87A)),
        ("lemon", "Lemon", Ink.Hex(0xF2B705)),
        ("black", "Midnight", Ink.Hex(0x1D1D21)),
        ("chrome", "Chrome", Ink.Hex(0x6B7380)),
        ("holo", "Holo", Ink.Purple),
        ("fuzzy", "Fuzzy", Ink.Purple),
        ("wood", "Wood", Ink.Hex(0x9A5E2C)),
        ("marble", "Marble", Ink.Hex(0x6B7380)),
        ("terrazzo", "Terrazzo", Ink.Hex(0xF0561D)),
        ("glass", "Glass", Ink.Hex(0x1597E5)),
        ("jelly", "Jelly", Ink.Hex(0xA33FE6)),
        ("pastel", "Pastel", Ink.Hex(0xEE4F98)),
        ("pixel", "Pixel", Ink.Hex(0x1D1D21)),
        ("lowpoly", "Low poly", Ink.Purple),
        ("knit", "Knit", Ink.Purple),
        ("skeleton", "Skeleton", Ink.Hex(0x9A5E2C)),
        ("astro", "Astronaut", Ink.Hex(0x2F86F0)),
        ("space", "Space", Ink.Red),
        ("monster", "Monster", Ink.Hex(0x3E9E2E)),
        ("water", "Water", Ink.Hex(0x1597E5)),
        ("lava", "Lava", Ink.Hex(0xF0561D)),
        ("grass", "Grass", Ink.Hex(0x3E9E2E)),
        ("cactus", "Cactus", Ink.Hex(0x3E9E2E)),
        ("choco", "Chocolate", Ink.Hex(0x9A5E2C)),
        ("cloud", "Cloud", Ink.Hex(0x2F86F0)),
        ("mech", "Mech", Ink.Hex(0xF0561D)),
        ("neon", "Neon", Ink.Hex(0xE0309A)),
        ("leaf", "Leaf", Ink.Hex(0x3E9E2E)),
        ("cookie", "Gingerbread", Ink.Hex(0x9A5E2C)),
    };

    private static readonly Dictionary<string, PointerStyle> Known = new()
    {
        ["lavender"] = Lavender,
        ["glove"] = Glove,
        ["coral"] = Coral,
        ["plush"] = Plush,
        ["robot"] = Robot,
        ["laser"] = Laser,
    };

    private static PointerStyle Make(string id)
    {
        if (!Known.TryGetValue(id, out PointerStyle? s))
        {
            s = new PointerStyle(id);
            Known[id] = s;
        }
        return s;
    }

    public static IEnumerable<PointerStyle> BuiltInHands => BuiltIn.Select(b => Make(b.id));

    public static IEnumerable<PointerStyle> CustomHandStyles => CustomHands.All.Select(h => Make(h.Id));

    public static List<PointerStyle> AllCases => BuiltInHands.Concat(CustomHandStyles).Append(Laser).ToList();

    public static readonly PointerStyle[] DefaultPinned = { Lavender, Glove, Coral, Plush, Robot, Laser };

    public static PointerStyle? FromId(string id)
    {
        if (id == "laser" || BuiltIn.Any(b => b.id == id) || CustomHands.Hand(id) != null)
        {
            return Make(id);
        }
        return null;
    }

    public bool IsLaser => Id == "laser";

    public bool IsCustom => CustomHands.Hand(Id) != null;

    public string Title
    {
        get
        {
            if (IsLaser)
            {
                return "Laser dot";
            }
            CustomHand? custom = CustomHands.Hand(Id);
            if (custom != null)
            {
                return custom.Name;
            }
            foreach (var b in BuiltIn)
            {
                if (b.id == Id)
                {
                    return b.title;
                }
            }
            return Id;
        }
    }

    /// File name of the hand image, without extension. Null for the laser dot.
    public string? AssetName => IsLaser ? null : IsCustom ? Id : $"hand_{Id}";

    public Color InkColor
    {
        get
        {
            if (IsLaser)
            {
                return Ink.Red;
            }
            CustomHand? custom = CustomHands.Hand(Id);
            if (custom != null)
            {
                return Ink.Hex(custom.Ink);
            }
            foreach (var b in BuiltIn)
            {
                if (b.id == Id)
                {
                    return b.ink;
                }
            }
            return Ink.Purple;
        }
    }

    public static PointerStyle Saved
    {
        get
        {
            string? raw = Preferences.Get<string?>("pointerStyle", null);
            return raw == null ? Lavender : FromId(raw) ?? Lavender;
        }
        set => Preferences.Set("pointerStyle", value.Id, notify: false);
    }

    public bool Equals(PointerStyle? other) => other is not null && other.Id == Id;

    public override bool Equals(object? obj) => obj is PointerStyle p && Equals(p);

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);

    public static bool operator ==(PointerStyle? a, PointerStyle? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(PointerStyle? a, PointerStyle? b) => !(a == b);

    public override string ToString() => Id;
}

/// A hand-on-a-stick image plus where its fingertip, wrist and rod are, measured from its alpha.
/// Vertical values are fractions of the image height from the top, horizontal ones of its width.
internal sealed class HandAsset
{
    public BitmapSource Image { get; }
    public double Aspect { get; }
    public double HandTop { get; }
    public double Tip { get; }
    public double Wrist { get; }
    public double RodEnd { get; }
    public double TipX { get; }
    public double RodX { get; }
    public double HandMinX { get; }
    public double HandMaxX { get; }

    private HandAsset(BitmapSource image, double aspect, double handTop, double tip, double wrist, double rodEnd,
                      double tipX, double rodX, double handMinX, double handMaxX)
    {
        Image = image;
        Aspect = aspect;
        HandTop = handTop;
        Tip = tip;
        Wrist = wrist;
        RodEnd = rodEnd;
        TipX = tipX;
        RodX = rodX;
        HandMinX = handMinX;
        HandMaxX = handMaxX;
    }

    private static readonly Dictionary<string, HandAsset> Cache = new();
    private static readonly List<string> Recent = new();
    private const int CacheLimit = 2;
    private static readonly Dictionary<string, BitmapSource> Thumbnails = new();
    private static readonly object ThumbGate = new();

    public static HandAsset? Load(string name)
    {
        if (Cache.TryGetValue(name, out HandAsset? hit))
        {
            Touch(name);
            return hit;
        }
        BitmapSource? image = LoadImage(name);
        HandAsset? asset = image == null ? null : Measure(image);
        if (asset == null)
        {
            return null;
        }
        Cache[name] = asset;
        Touch(name);
        return asset;
    }

    private static void Touch(string name)
    {
        Recent.Remove(name);
        Recent.Add(name);
        while (Recent.Count > CacheLimit)
        {
            Cache.Remove(Recent[0]);
            Recent.RemoveAt(0);
        }
    }

    public static void Forget(string name)
    {
        Cache.Remove(name);
        Recent.Remove(name);
        lock (ThumbGate)
        {
            Thumbnails.Remove(name);
        }
    }

    public static void PreloadThumbnails(IEnumerable<string> names, Action? done = null)
    {
        string[] missing;
        lock (ThumbGate)
        {
            missing = names.Where(n => !Thumbnails.ContainsKey(n)).ToArray();
        }
        if (missing.Length == 0)
        {
            done?.Invoke();
            return;
        }
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        // WPF imaging and drawing want an STA thread.
        var worker = new System.Threading.Thread(() =>
        {
            foreach (string name in missing)
            {
                try
                {
                    BitmapSource? image = LoadImage(name);
                    BitmapSource? thumb = image == null ? null : Measure(image)?.MakeThumbnail();
                    if (thumb != null)
                    {
                        lock (ThumbGate)
                        {
                            Thumbnails.TryAdd(name, thumb);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Write($"thumbnail for {name} failed: {e.Message}");
                }
            }
            if (done != null)
            {
                dispatcher.BeginInvoke(done);
            }
        })
        {
            IsBackground = true,
            Name = "TinyHand thumbnails",
            Priority = System.Threading.ThreadPriority.BelowNormal,
        };
        worker.SetApartmentState(System.Threading.ApartmentState.STA);
        worker.Start();
    }

    public static BitmapSource? Thumbnail(string name)
    {
        lock (ThumbGate)
        {
            if (Thumbnails.TryGetValue(name, out BitmapSource? hit))
            {
                return hit;
            }
        }
        HandAsset? asset = Cache.TryGetValue(name, out HandAsset? cached) ? cached : LoadImage(name) is { } img ? Measure(img) : null;
        BitmapSource? thumb = asset?.MakeThumbnail();
        if (thumb != null)
        {
            lock (ThumbGate)
            {
                Thumbnails[name] = thumb;
            }
        }
        return thumb;
    }

    /// A square crop around the hand, 128 px, for buttons and tiles.
    private BitmapSource MakeThumbnail()
    {
        double h = Image.PixelHeight;
        double w = Image.PixelWidth;
        double handW = (HandMaxX - HandMinX) * w;
        double handH = (Wrist - HandTop) * h;
        double side = Math.Max(handW, handH) * 1.06;
        double cx = (HandMinX + HandMaxX) / 2 * w;
        double cy = (HandTop + Wrist) / 2 * h;
        var rect = new Rect(cx - side / 2, cy - side / 2, side, side);
        rect.Intersect(new Rect(0, 0, w, h));
        var crop = new CroppedBitmap(Image, new Int32Rect((int)rect.X, (int)rect.Y, Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height)));
        const int px = 128;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (DrawingContext dc = visual.RenderOpen())
        {
            // Keep the crop square in the output even when it was clipped at an image edge.
            double scale = px / side;
            dc.DrawImage(crop, new Rect((px - rect.Width * scale) / 2, (px - rect.Height * scale) / 2, rect.Width * scale, rect.Height * scale));
        }
        var target = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// Downscales an image to the given height with a high-quality filter.
    public static BitmapSource Resized(BitmapSource image, int height)
    {
        int width = Math.Max(1, (int)Math.Round((double)image.PixelWidth * height / image.PixelHeight));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, height));
        }
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static HandAsset? Measure(BitmapSource image)
    {
        if (image.Format != PixelFormats.Bgra32 && image.Format != PixelFormats.Pbgra32)
        {
            image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        }
        int w = image.PixelWidth;
        int h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);

        bool Solid(int x, int y) => pixels[(y * w + x) * 4 + 3] > 128;

        var counts = new int[h];
        for (int y = 0; y < h; y++)
        {
            int n = 0;
            for (int x = 0; x < w; x++)
            {
                if (Solid(x, y)) n++;
            }
            counts[y] = n;
        }
        double Center(int y)
        {
            int lo = -1, hi = -1;
            for (int x = 0; x < w; x++)
            {
                if (Solid(x, y))
                {
                    if (lo < 0) lo = x;
                    hi = x;
                }
            }
            return lo < 0 ? 0.5 : (lo + hi) / 2.0 / w;
        }

        int top = Array.FindIndex(counts, c => c > 0);
        if (top < 0)
        {
            return null;
        }
        int searchEnd = Math.Min(h, top + (int)(0.45 * h));
        int maxW = 0;
        for (int y = top; y < searchEnd; y++) maxW = Math.Max(maxW, counts[y]);
        if (maxW == 0)
        {
            return null;
        }
        int fist = -1;
        for (int y = top; y < h; y++)
        {
            if (counts[y] > 0.6 * maxW) { fist = y; break; }
        }
        if (fist < 0) return null;
        int wrist = -1;
        for (int y = fist; y < h; y++)
        {
            if (counts[y] < 0.35 * maxW) { wrist = y; break; }
        }
        if (wrist < 0) return null;
        int rodW = counts[Math.Min(h - 1, wrist + h / 50)];
        int lastRow = Array.FindLastIndex(counts, c => c > 0);
        if (lastRow < 0) lastRow = h - 1;
        int from = Math.Min(h - 1, wrist + h / 33);
        int rodEnd = lastRow;
        for (int y = from; y < h; y++)
        {
            if (counts[y] > 1.8 * rodW) { rodEnd = y; break; }
        }

        int minX = w, maxX = 0;
        for (int y = top; y < wrist; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (Solid(x, y))
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }
        }

        if (!image.IsFrozen && image.CanFreeze)
        {
            image.Freeze();
        }
        double fh = h;
        return new HandAsset(
            image,
            aspect: w / fh,
            handTop: Math.Max(0, top - 0.004 * fh) / fh,
            tip: top / fh,
            wrist: wrist / fh,
            rodEnd: rodEnd / fh,
            tipX: Center(Math.Min(h - 1, top + h / 200)),
            rodX: Center((wrist + rodEnd) / 2),
            handMinX: minX / (double)w,
            handMaxX: maxX / (double)w);
    }

    private static BitmapSource? LoadImage(string name)
    {
        var candidates = new List<string> { Path.Combine(CustomHands.Directory, name + ".png") };
        if (Paths.Asset(name + ".png") is { } bundled)
        {
            candidates.Add(bundled);
        }
        foreach (string path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                return LoadBitmap(path);
            }
            catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or ArgumentException)
            {
                Log.Write($"could not read {path}: {e.Message}");
            }
        }
        return null;
    }

    public static BitmapSource LoadBitmap(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        BitmapSource converted = frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var copy = new WriteableBitmap(converted);
        copy.Freeze();
        return copy;
    }
}
