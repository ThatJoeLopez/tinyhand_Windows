using System;
using System.Buffers.Binary;
using System.IO;

namespace TinyHand;

/// Opens image files for decoding, with PNG metadata chunks removed.
///
/// Many of the hand images carry a `caBX` chunk (C2PA content credentials, added by AI image
/// tools, including the ones people use to make their own hands). Windows' PNG decoder returns
/// a blank image for these files, so everything but the chunks needed to draw the picture is
/// dropped before decoding. Other formats are passed through untouched.
internal static class PngCleaner
{
    private static ReadOnlySpan<byte> Signature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // Chunks that affect the pixels. Everything else is metadata.
    private static readonly string[] Keep = { "IHDR", "PLTE", "tRNS", "IDAT", "IEND", "gAMA", "sRGB", "cHRM", "iCCP", "sBIT" };

    public static Stream Open(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return new MemoryStream(Clean(data), writable: false);
    }

    public static byte[] Clean(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return data;
        }
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8);
        int i = 8;
        bool dropped = false;
        while (i + 12 <= data.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i, 4));
            if (length < 0 || i + 12 + length > data.Length)
            {
                // Truncated or damaged: let the decoder deal with the original.
                return data;
            }
            string type = System.Text.Encoding.ASCII.GetString(data, i + 4, 4);
            if (Array.IndexOf(Keep, type) >= 0)
            {
                output.Write(data, i, 12 + length);
            }
            else
            {
                dropped = true;
            }
            i += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }
        return dropped ? output.ToArray() : data;
    }
}
