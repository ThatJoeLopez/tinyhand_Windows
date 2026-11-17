using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TinyHand;

/// Reads PNG files without Windows' imaging decoder.
///
/// On some Windows machines the system PNG decoder gives back blank images for most of the
/// bundled hands (the large 2 MB ones), while it reads the small ones fine. Decoding the PNG
/// here, with .NET's own zlib, gives the same pixels everywhere. Anything this reader doesn't
/// handle (interlaced or 16-bit PNGs, other formats) falls back to the system decoder.
internal static class PngCleaner
{
    private static ReadOnlySpan<byte> Signature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// Decodes a PNG to straight-alpha BGRA, or returns null if it's not a PNG this can read.
    public static BitmapSource? TryDecode(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        try
        {
            return Decode(data);
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            Log.Write($"own PNG reader couldn't read {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    private static BitmapSource? Decode(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return null;
        }
        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        using var compressed = new MemoryStream();
        int i = 8;
        while (i + 12 <= data.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i, 4)));
            if (i + 12 + length > data.Length)
            {
                throw new InvalidDataException("truncated chunk");
            }
            string type = System.Text.Encoding.ASCII.GetString(data, i + 4, 4);
            var body = new ReadOnlySpan<byte>(data, i + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(body);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                    bitDepth = body[8];
                    colorType = body[9];
                    interlace = body[12];
                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    paletteAlpha = body.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(body);
                    break;
            }
            i += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }

        int channels = colorType switch
        {
            0 => 1, // grey
            2 => 3, // RGB
            3 => 1, // palette
            4 => 2, // grey + alpha
            6 => 4, // RGBA
            _ => 0,
        };
        if (bitDepth != 8 || interlace != 0 || channels == 0 || width <= 0 || height <= 0 || width > 16384 || height > 16384
            || (colorType == 3 && palette == null))
        {
            return null;
        }

        int stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (var z = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n == 0)
                {
                    throw new InvalidDataException("image data ended early");
                }
                read += n;
            }
        }

        // Undo the per-row filters.
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[y * (stride + 1)];
            int src = y * (stride + 1) + 1;
            int row = y * stride;
            int prev = row - stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? pixels[row + x - channels] : 0;
                int b = y > 0 ? pixels[prev + x] : 0;
                int c = x >= channels && y > 0 ? pixels[prev + x - channels] : 0;
                int v = raw[src + x];
                pixels[row + x] = filter switch
                {
                    0 => (byte)v,
                    1 => (byte)(v + a),
                    2 => (byte)(v + b),
                    3 => (byte)(v + ((a + b) >> 1)),
                    4 => (byte)(v + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"unknown filter {filter}"),
                };
            }
        }

        // To straight-alpha BGRA.
        var bgra = new byte[width * height * 4];
        for (int p = 0, o = 0; p < width * height; p++, o += 4)
        {
            int s = p * channels;
            byte r, g, bl, al;
            switch (colorType)
            {
                case 6:
                    r = pixels[s]; g = pixels[s + 1]; bl = pixels[s + 2]; al = pixels[s + 3];
                    break;
                case 2:
                    r = pixels[s]; g = pixels[s + 1]; bl = pixels[s + 2]; al = 255;
                    break;
                case 4:
                    r = g = bl = pixels[s]; al = pixels[s + 1];
                    break;
                case 0:
                    r = g = bl = pixels[s]; al = 255;
                    break;
                default:
                    int index = pixels[s];
                    r = palette![index * 3]; g = palette[index * 3 + 1]; bl = palette[index * 3 + 2];
                    al = paletteAlpha != null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
                    break;
            }
            bgra[o] = bl;
            bgra[o + 1] = g;
            bgra[o + 2] = r;
            bgra[o + 3] = al;
        }

        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        image.Freeze();
        return image;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
