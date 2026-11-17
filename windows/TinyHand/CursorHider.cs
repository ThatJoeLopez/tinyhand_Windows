using System;
using System.IO;

namespace TinyHand;

/// Hides the system cursor everywhere while the hand is on.
///
/// Windows has no "hide the cursor for every app" call, so this swaps each standard system
/// cursor for a blank one and restores the user's cursor scheme afterwards. A marker file
/// records that the cursors are swapped, so a crash is cleaned up on the next launch.
internal static class CursorHider
{
    private static readonly uint[] CursorIds =
    {
        32512, // arrow
        32513, // I-beam
        32514, // wait
        32515, // cross
        32516, // up arrow
        32631, // pen
        32640, // size
        32641, // icon
        32642, // size NW-SE
        32643, // size NE-SW
        32644, // size W-E
        32645, // size N-S
        32646, // size all
        32648, // no
        32649, // hand
        32650, // app starting
        32651, // help
        32671, // pin
        32672, // person
    };

    private static bool hidden;
    private static DateTime hiddenAt = DateTime.MinValue;

    private static string MarkerPath => Path.Combine(Paths.LocalData, "cursor-hidden");

    public static bool IsHidden => hidden;

    public static bool IsSettling => hidden && (DateTime.UtcNow - hiddenAt).TotalSeconds < 1;

    /// Restores the cursors if a previous run crashed while they were hidden.
    public static void RecoverFromCrash()
    {
        if (File.Exists(MarkerPath))
        {
            Log.Write("restoring cursors left hidden by an earlier run");
            Restore();
        }
    }

    public static void Hide()
    {
        if (hidden)
        {
            return;
        }
        hidden = true;
        hiddenAt = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(Paths.LocalData);
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("o"));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        foreach (uint id in CursorIds)
        {
            // SetSystemCursor takes ownership of the cursor, so each id gets its own copy.
            IntPtr blank = BlankCursor();
            if (blank != IntPtr.Zero && !Native.SetSystemCursor(blank, id))
            {
                Native.DestroyIcon(blank);
            }
        }
    }

    public static void Show()
    {
        if (!hidden)
        {
            return;
        }
        hidden = false;
        Restore();
    }

    private static void Restore()
    {
        Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        try
        {
            File.Delete(MarkerPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static IntPtr BlankCursor()
    {
        const int size = 32;
        var and = new byte[size * size / 8];
        var xor = new byte[size * size / 8];
        Array.Fill(and, (byte)0xFF);
        return Native.CreateCursor(IntPtr.Zero, 0, 0, size, size, and, xor);
    }
}
