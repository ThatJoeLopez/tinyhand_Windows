using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TinyHand;

/// Checks an image of a hand and puts it on a stick, so people can add their own pointers.
internal static class HandMaker
{
    public const string PromptTemplate = """
    Make a pointer image for TinyHand, an app that shows a toy hand on screen.

    Draw one cartoon right hand in this style: [describe your hand, e.g. knitted wool in mustard yellow]

    Rules:
    - We see the BACK of the hand. The palm faces away from us. The knuckles and the smooth back of the hand face the viewer.
    - The index finger points straight up. The other three fingers are folded down, so we only see their knuckles, never their fingertips or fingernails.
    - The raised index finger sits at the far left of the knuckle row, right next to the thumb. Three folded knuckles sit to its right. It must never look like a middle finger.
    - The thumb is tucked against the left side of the hand. Nothing crosses the front of the fist: no thumb, no fingertips, no fingernails, no palm.
    - The fingertip is the highest point of the image. Nothing above or beside it.
    - The hand ends at the bottom in a wrist cuff or a clean flat wrist, facing straight down, centred.
    - Upright, not tilted. Whole hand visible, nothing cropped.
    - Transparent background. No shadow, no floor, no outline glow, no text, no stick, no arm.
    - Soft 3D toy look, bright and readable at small sizes.
    - Portrait image, hand filling most of the height with a little empty space around it.
    """;

    public static string Prompt(string idea)
    {
        string trimmed = idea.Trim();
        string style = trimmed.Length == 0 ? "a soft vinyl toy in a bright colour" : trimmed;
        return PromptTemplate.Replace("[describe your hand, e.g. knitted wool in mustard yellow]", style);
    }

    public sealed class Problem : Exception
    {
        public Problem(string message) : base(message)
        {
        }
    }

    private sealed class Pixels
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }

        public Pixels(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public byte Alpha(int x, int y) => Bgra[(y * Width + x) * 4 + 3];
    }

    private readonly record struct Box(int MinX, int MinY, int MaxX, int MaxY);

    private static readonly PixelFormat[] Opaque =
    {
        PixelFormats.Bgr24, PixelFormats.Bgr32, PixelFormats.Rgb24, PixelFormats.Bgr101010, PixelFormats.Bgr555,
        PixelFormats.Bgr565, PixelFormats.Rgb48, PixelFormats.Gray2, PixelFormats.Gray4, PixelFormats.Gray8,
        PixelFormats.Gray16, PixelFormats.Gray32Float, PixelFormats.BlackWhite, PixelFormats.Cmyk32, PixelFormats.Rgb128Float,
    };

    public static (BitmapSource image, uint ink) Make(string path)
    {
        BitmapSource source;
        try
        {
            if (PngCleaner.TryDecode(path) is BitmapSource own)
            {
                return Finish(own);
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (Opaque.Contains(frame.Format))
            {
                throw new Problem("The image needs a transparent background. Save it as a PNG with transparency.");
            }
            source = frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or ArgumentException or UnauthorizedAccessException)
        {
            throw new Problem("That file isn't an image TinyHand can read.");
        }
        return Finish(source);
    }

    private static (BitmapSource image, uint ink) Finish(BitmapSource source)
    {
        Pixels pixels = Read(source);
        Check(pixels);
        BitmapSource composed = Compose(source, pixels) ?? throw new Problem("TinyHand couldn't put that hand on a stick.");
        return (composed, InkColor(pixels));
    }

    private static Pixels Read(BitmapSource image)
    {
        int w = image.PixelWidth;
        int h = image.PixelHeight;
        if (w < 64 || h < 64)
        {
            throw new Problem("The image is too small. Use one at least 512 pixels tall.");
        }
        var bgra = new byte[w * h * 4];
        image.CopyPixels(bgra, w * 4, 0);
        return new Pixels(w, h, bgra);
    }

    private static void Check(Pixels p)
    {
        int w = p.Width;
        int h = p.Height;
        int edge = 0;
        int edgeSolid = 0;
        for (int x = 0; x < w; x++)
        {
            edge += 2;
            edgeSolid += (p.Alpha(x, 0) > 32 ? 1 : 0) + (p.Alpha(x, h - 1) > 32 ? 1 : 0);
        }
        for (int y = 0; y < h; y++)
        {
            edge += 2;
            edgeSolid += (p.Alpha(0, y) > 32 ? 1 : 0) + (p.Alpha(w - 1, y) > 32 ? 1 : 0);
        }
        if ((double)edgeSolid / edge > 0.02)
        {
            throw new Problem("The background isn't transparent, or the hand touches the edge of the image.");
        }

        int step = Math.Max(1, Math.Min(w, h) / 256);
        int gw = w / step;
        int gh = h / step;
        var grid = new bool[gw * gh];
        for (int gy = 0; gy < gh; gy++)
        {
            for (int gx = 0; gx < gw; gx++)
            {
                grid[gy * gw + gx] = p.Alpha(gx * step, gy * step) > 128;
            }
        }
        var seen = new bool[gw * gh];
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (int start = 0; start < grid.Length; start++)
        {
            if (!grid[start] || seen[start])
            {
                continue;
            }
            stack.Push(start);
            seen[start] = true;
            int n = 0;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                n++;
                int x = i % gw;
                int y = i / gw;
                foreach ((int nx, int ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= gw || ny >= gh)
                    {
                        continue;
                    }
                    int j = ny * gw + nx;
                    if (grid[j] && !seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }
            sizes.Add(n);
        }
        if (sizes.Count == 0)
        {
            throw new Problem("The image is empty.");
        }
        if ((double)sizes.Max() / sizes.Sum() < 0.97)
        {
            throw new Problem("The image has more than one thing in it. Use a single hand.");
        }

        Box box = Bounds(p) ?? throw new Problem("The image is empty.");
        int bw = box.MaxX - box.MinX + 1;
        int bh = box.MaxY - box.MinY + 1;
        double aspect = (double)bh / bw;
        if (aspect < 1.05 || aspect > 2.8)
        {
            throw new Problem("The hand should stand upright, taller than it is wide.");
        }
        if ((double)bh / h < 0.5)
        {
            throw new Problem("The hand is too small in the image. Crop it closer.");
        }

        int topBand = Math.Max(2, bh * 8 / 100);
        int fingerMin = w;
        int fingerMax = 0;
        for (int y = box.MinY; y < box.MinY + topBand; y++)
        {
            for (int x = box.MinX; x <= box.MaxX; x++)
            {
                if (p.Alpha(x, y) > 128)
                {
                    fingerMin = Math.Min(fingerMin, x);
                    fingerMax = Math.Max(fingerMax, x);
                }
            }
        }
        if ((double)(fingerMax - fingerMin + 1) / bw > 0.4)
        {
            throw new Problem("The top of the image should be a single finger pointing up.");
        }

        if ((double)BottomSpan(p, box).width / bw < 0.2)
        {
            throw new Problem("The hand should end in a flat wrist or cuff at the bottom.");
        }

        int soft = 0;
        int any = 0;
        for (int y = 0; y < h; y += 2)
        {
            for (int x = 0; x < w; x += 2)
            {
                byte a = p.Alpha(x, y);
                if (a > 20)
                {
                    any++;
                    if (a < 200)
                    {
                        soft++;
                    }
                }
            }
        }
        if ((double)soft / Math.Max(1, any) > 0.08)
        {
            throw new Problem("The hand has a soft shadow or glow around it. Ask for no shadow.");
        }
    }

    private static Box? Bounds(Pixels p)
    {
        int minX = p.Width, minY = p.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < p.Height; y++)
        {
            for (int x = 0; x < p.Width; x++)
            {
                if (p.Alpha(x, y) > 128)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }
        return maxX < 0 ? null : new Box(minX, minY, maxX, maxY);
    }

    private static (int width, double center) BottomSpan(Pixels p, Box box)
    {
        int bh = box.MaxY - box.MinY + 1;
        int lo = p.Width;
        int hi = -1;
        for (int y = Math.Max(box.MinY, box.MaxY - Math.Max(2, bh * 4 / 100)); y <= box.MaxY; y++)
        {
            for (int x = box.MinX; x <= box.MaxX; x++)
            {
                if (p.Alpha(x, y) > 128)
                {
                    lo = Math.Min(lo, x);
                    hi = Math.Max(hi, x);
                }
            }
        }
        return hi < 0 ? (0, (box.MinX + box.MaxX) / 2.0) : (hi - lo + 1, (lo + hi) / 2.0);
    }

    /// Lays the hand out like the built-in ones: 1024×1536, fingertip near the top, on a black rod.
    private static BitmapSource? Compose(BitmapSource image, Pixels p)
    {
        if (Bounds(p) is not Box box)
        {
            return null;
        }
        const int canvasW = 1024;
        const int canvasH = 1536;
        const double handTop = 150;
        const double handHeight = 560;
        double bw = box.MaxX - box.MinX + 1;
        double bh = box.MaxY - box.MinY + 1;
        double scale = Math.Min(handHeight / bh, 700 / bw);
        double drawW = p.Width * scale;
        double drawH = p.Height * scale;
        double wristX = (BottomSpan(p, box).center + 0.5) * scale;
        double originX = canvasW / 2.0 - wristX;
        double originTop = handTop - box.MinY * scale;
        double handBottom = handTop + bh * scale;

        double rodW = Math.Min(76, Math.Max(54, bw * scale * 0.18));
        double rodTop = handBottom - 30;
        const double rodBottom = 1462;
        var rod = new Rect(canvasW / 2.0 - rodW / 2, rodTop, rodW, rodBottom - rodTop);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (DrawingContext dc = visual.RenderOpen())
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(Ink.Hex(0x000000), 0),
                    new GradientStop(Ink.Hex(0x121214), 0.22),
                    new GradientStop(Ink.Hex(0x3A3A3E), 0.45),
                    new GradientStop(Ink.Hex(0x121214), 0.75),
                    new GradientStop(Ink.Hex(0x000000), 1),
                },
            };
            dc.DrawRoundedRectangle(gradient, null, rod, rodW / 2, rodW / 2);
            dc.DrawImage(image, new Rect(originX, originTop, drawW, drawH));
        }
        var target = new RenderTargetBitmap(canvasW, canvasH, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// Picks a lively, mid-bright colour from the hand for its ink.
    private static uint InkColor(Pixels p)
    {
        (double score, double r, double g, double b) best = (-1, 0.53, 0.33, 0.95);
        var buckets = new Dictionary<int, (int n, double r, double g, double b)>();
        for (int y = 0; y < p.Height; y += 3)
        {
            for (int x = 0; x < p.Width; x += 3)
            {
                int i = (y * p.Width + x) * 4;
                if (p.Bgra[i + 3] <= 200)
                {
                    continue;
                }
                double r = p.Bgra[i + 2] / 255.0;
                double g = p.Bgra[i + 1] / 255.0;
                double b = p.Bgra[i] / 255.0;
                int key = (int)Math.Min(4, r * 5) * 36 + (int)Math.Min(4, g * 5) * 6 + (int)Math.Min(4, b * 5);
                buckets.TryGetValue(key, out var e);
                buckets[key] = (e.n + 1, e.r + r, e.g + g, e.b + b);
            }
        }
        foreach (var e in buckets.Values)
        {
            double r = e.r / e.n;
            double g = e.g / e.n;
            double b = e.b / e.n;
            double hi = Math.Max(r, Math.Max(g, b));
            double lo = Math.Min(r, Math.Min(g, b));
            double sat = hi == 0 ? 0 : (hi - lo) / hi;
            double lum = 0.3 * r + 0.59 * g + 0.11 * b;
            double score = e.n * (0.25 + sat) * (lum > 0.12 && lum < 0.85 ? 1 : 0.15);
            if (score > best.score)
            {
                best = (score, r, g, b);
            }
        }
        double max = Math.Max(best.r, Math.Max(best.g, best.b));
        double min = Math.Min(best.r, Math.Min(best.g, best.b));
        double rr = best.r, gg = best.g, bb = best.b;
        if (max - min < 0.08)
        {
            rr = 0.42;
            gg = 0.45;
            bb = 0.5;
        }
        else if (max > 0.82)
        {
            rr *= 0.82 / max;
            gg *= 0.82 / max;
            bb *= 0.82 / max;
        }
        uint C(double v) => (uint)Math.Min(255, v * 255);
        return C(rr) << 16 | C(gg) << 8 | C(bb);
    }

    public static void Write(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            encoder.Save(stream);
        }
        File.Move(tmp, path, overwrite: true);
    }
}

internal sealed class CustomHand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public uint Ink { get; set; }
}

internal static class CustomHands
{
    public static event Action? Changed;

    public static string Directory =>
        Environment.GetEnvironmentVariable("TH_HANDS_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Paths.Data, "Hands");

    private static string ListPath => Path.Combine(Directory, "hands.json");

    private static List<CustomHand>? loaded;

    public static List<CustomHand> All
    {
        get
        {
            if (loaded != null)
            {
                return loaded;
            }
            try
            {
                loaded = File.Exists(ListPath)
                    ? JsonSerializer.Deserialize<List<CustomHand>>(File.ReadAllText(ListPath), JsonOptions) ?? new List<CustomHand>()
                    : new List<CustomHand>();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Write($"hands.json unreadable: {e.Message}");
                loaded = new List<CustomHand>();
            }
            return loaded;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static CustomHand? Hand(string id) => id.StartsWith("custom-", StringComparison.Ordinal) ? All.FirstOrDefault(h => h.Id == id) : null;

    public static PointerStyle Add(BitmapSource image, uint ink, string name)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        HandMaker.Write(image, Path.Combine(Directory, id + ".png"));
        string trimmed = name.Trim();
        var list = All.ToList();
        list.Add(new CustomHand
        {
            Id = id,
            Name = trimmed.Length == 0 ? $"My hand {list.Count + 1}" : trimmed[..Math.Min(16, trimmed.Length)],
            Ink = ink,
        });
        Save(list);
        return PointerStyle.FromId(id) ?? throw new HandMaker.Problem("TinyHand couldn't add that hand.");
    }

    public static void Remove(PointerStyle style)
    {
        if (!style.IsCustom || style.AssetName is not string asset)
        {
            return;
        }
        try
        {
            File.Delete(Path.Combine(Directory, asset + ".png"));
        }
        catch (IOException)
        {
        }
        HandAsset.Forget(asset);
        Save(All.Where(h => h.Id != style.Id).ToList());
    }

    private static void Save(List<CustomHand> list)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(ListPath, JsonSerializer.Serialize(list, JsonOptions));
        loaded = list;
        Changed?.Invoke();
    }

    public static string SuggestedName(string path)
    {
        string baseName = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
        string words = string.Join(" ", baseName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        if (words.Length == 0 || words.Length > 16 || words.ToLowerInvariant().StartsWith("chatgpt image", StringComparison.Ordinal))
        {
            return $"My hand {All.Count + 1}";
        }
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
