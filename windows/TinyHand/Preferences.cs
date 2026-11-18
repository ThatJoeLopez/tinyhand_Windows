using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Windows.Media;
using Microsoft.Win32;

namespace TinyHand;

/// A global keyboard shortcut: a virtual-key code plus MOD_* flags.
internal readonly record struct Shortcut(uint Key, uint Modifiers)
{
    public static readonly Shortcut ToggleDefault = new(0x48 /* H */, Native.MOD_CONTROL | Native.MOD_ALT);
    public static readonly Shortcut ClearDefault = new(0x58 /* X */, Native.MOD_CONTROL | Native.MOD_ALT);

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((Modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }
    }

    public string[] Parts => Display.Split('+');

    public static string KeyName(uint vk)
    {
        if (vk >= 0x30 && vk <= 0x5A)
        {
            return ((char)vk).ToString();
        }
        if (vk >= 0x70 && vk <= 0x87)
        {
            return "F" + (vk - 0x6F);
        }
        if (vk >= 0x60 && vk <= 0x69)
        {
            return "Num" + (vk - 0x60);
        }
        return vk switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            0x13 => "Pause",
            0x91 => "ScrollLock",
            _ => $"Key{vk:X2}",
        };
    }
}

/// The key that, while held, lets clicks reach the app (Option on the Mac).
internal enum InteractKey
{
    Alt,
    Control,
    Off,
}

internal static class InteractKeyInfo
{
    public static string Title(this InteractKey k) => k switch
    {
        InteractKey.Alt => "Alt",
        InteractKey.Control => "Ctrl",
        _ => "Off",
    };

    public static int VirtualKey(this InteractKey k) => k switch
    {
        InteractKey.Alt => Native.VK_MENU,
        InteractKey.Control => Native.VK_CONTROL,
        _ => 0,
    };
}

internal enum InkChoice
{
    Match,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    White,
    Black,
}

internal static class InkChoiceInfo
{
    public static string Title(this InkChoice c) => c == InkChoice.Match ? "Match the pointer" : c.ToString();

    public static Color? Color(this InkChoice c) => c switch
    {
        InkChoice.Red => Ink.Red,
        InkChoice.Orange => Ink.Hex(0xFF8A00),
        InkChoice.Yellow => Ink.Hex(0xFFCC00),
        InkChoice.Green => Ink.Hex(0x30C25A),
        InkChoice.Blue => Ink.Hex(0x0A84FF),
        InkChoice.Purple => Ink.Hex(0x8754F2),
        InkChoice.White => Ink.Hex(0xFFFFFF),
        InkChoice.Black => Ink.Hex(0x1D1D21),
        _ => null,
    };

    public static Color Resolved(this InkChoice c, PointerStyle style) => c.Color() ?? style.InkColor;
}

internal enum TapSoundChoice
{
    Thock,
    Click,
    Tock,
    Clack,
    Typewriter,
    Snap,
    Wood,
    Pop,
    Boop,
    Blip,
    Coin,
    Pew,
    Squeak,
    Quack,
    Chirp,
}

internal static class TapSoundChoiceInfo
{
    public static string Title(this TapSoundChoice c) => c == TapSoundChoice.Wood ? "Wood block" : c.ToString();

    public static string FileName(this TapSoundChoice c) => $"tap_{c.ToString().ToLowerInvariant()}.wav";
}

/// Settings, stored as JSON in %APPDATA%\TinyHand\settings.json.
internal static class Preferences
{
    public static event Action? Changed;

    private static readonly object Gate = new();
    private static JsonObject? store;

    private static string FilePath => Path.Combine(Paths.Data, "settings.json");

    // JsonNode needs an explicit resolver to write values made with JsonValue.Create<T>.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonObject Store
    {
        get
        {
            if (store != null)
            {
                return store;
            }
            try
            {
                if (File.Exists(FilePath))
                {
                    store = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Write($"settings unreadable, starting fresh: {e.Message}");
            }
            store ??= new JsonObject();
            return store;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Store.ToJsonString(WriteOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"settings save failed: {e.Message}");
        }
    }

    public static T Get<T>(string key, T fallback)
    {
        lock (Gate)
        {
            try
            {
                JsonNode? node = Store[key];
                return node == null ? fallback : node.GetValue<T>() ?? fallback;
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException)
            {
                return fallback;
            }
        }
    }

    public static void Set<T>(string key, T value, bool notify = true)
    {
        lock (Gate)
        {
            Store[key] = JsonValue.Create(value);
            Save();
        }
        if (notify)
        {
            Changed?.Invoke();
        }
    }

    public static bool Has(string key)
    {
        lock (Gate)
        {
            return Store.ContainsKey(key);
        }
    }

    public static void Notify() => Changed?.Invoke();

    private static TEnum GetEnum<TEnum>(string key, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse(Get<string?>(key, null), true, out TEnum v) && Enum.IsDefined(v) ? v : fallback;

    public static Shortcut ToggleShortcut
    {
        get => LoadShortcut("shortcut.toggle") ?? Shortcut.ToggleDefault;
        set => SaveShortcut(value, "shortcut.toggle");
    }

    public static Shortcut ClearShortcut
    {
        get => LoadShortcut("shortcut.clear") ?? Shortcut.ClearDefault;
        set => SaveShortcut(value, "shortcut.clear");
    }

    public const double PointerSizeMin = 0.08;
    public const double PointerSizeMax = 0.20;

    public static double PointerSize
    {
        get => Math.Clamp(Get("pointerSize", 0.13), PointerSizeMin, PointerSizeMax);
        set => Set("pointerSize", value);
    }

    public static bool ShowShadow
    {
        get => Get("showShadow", true);
        set => Set("showShadow", value);
    }

    public static double FadeSeconds
    {
        get => Get("fadeSeconds", 3.0);
        set => Set("fadeSeconds", value);
    }

    public static bool HandSound
    {
        get => Get("sound.hands", true);
        set => Set("sound.hands", value);
    }

    public static TapSoundChoice TapSound
    {
        get => GetEnum("tapSound", TapSoundChoice.Thock);
        set => Set("tapSound", value.ToString().ToLowerInvariant());
    }

    public static bool LaserSound
    {
        get => Get("sound.laser", false);
        set => Set("sound.laser", value);
    }

    public static bool InteractKeepsHand
    {
        get => Get("interactKeepsHand", false);
        set => Set("interactKeepsHand", value);
    }

    public static bool DoubleShiftClickThrough
    {
        get => Get("doubleShiftClickThrough", true);
        set => Set("doubleShiftClickThrough", value);
    }

    public static InteractKey InteractKey
    {
        get => GetEnum("interactKey", InteractKey.Alt);
        set => Set("interactKey", value.ToString().ToLowerInvariant());
    }

    public static InkChoice InkChoice
    {
        get => GetEnum("inkChoice", InkChoice.Match);
        set => Set("inkChoice", value.ToString().ToLowerInvariant());
    }

    public static bool ShowToolbar
    {
        get => Get("showToolbar", true);
        set => Set("showToolbar", value);
    }

    public static bool ShowInkButton
    {
        get => Get("showInkButton", true);
        set => Set("showInkButton", value);
    }

    public const int MaxPinned = 8;

    public static List<PointerStyle> PinnedStyles
    {
        get
        {
            string? raw = Get<string?>("pinnedStyles", null);
            if (raw == null)
            {
                return PointerStyle.DefaultPinned.ToList();
            }
            var pinned = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(PointerStyle.FromId)
                .OfType<PointerStyle>()
                .Distinct()
                .ToList();
            return pinned.Count == 0 ? PointerStyle.DefaultPinned.ToList() : pinned;
        }
        set => Set("pinnedStyles", string.Join(",", value.Select(s => s.Id)));
    }

    public static bool FlagSet(string key) => Get(key, false);

    public static void SetFlag(string key) => Set(key, true, notify: false);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool LaunchAtLogin
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("TinyHand") is string;
        }
        set
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                {
                    key.SetValue("TinyHand", $"\"{Paths.ExePath}\" --background");
                }
                else
                {
                    key.DeleteValue("TinyHand", throwOnMissingValue: false);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Log.Write($"launch at login change failed: {e.Message}");
            }
            Changed?.Invoke();
        }
    }

    /// Keeps the login item pointing at this exe if the user moved TinyHand.
    public static void RefreshLaunchAtLoginPath()
    {
        if (LaunchAtLogin)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue("TinyHand", $"\"{Paths.ExePath}\" --background");
        }
    }

    public static void ResetAll()
    {
        string[] keys =
        {
            "shortcut.toggle", "shortcut.clear", "pointerSize", "showShadow", "fadeSeconds",
            "sound.hands", "sound.laser", "interactKey", "interactKeepsHand", "doubleShiftClickThrough",
            "pinnedStyles", "pointerStyle", "inkChoice", "showInkButton", "showToolbar", "tapSound",
        };
        lock (Gate)
        {
            foreach (string k in keys)
            {
                Store.Remove(k);
            }
            Save();
        }
        Changed?.Invoke();
    }

    private static Shortcut? LoadShortcut(string key)
    {
        string? raw = Get<string?>(key, null);
        if (raw == null)
        {
            return null;
        }
        string[] parts = raw.Split(':');
        if (parts.Length == 2 && uint.TryParse(parts[0], out uint vk) && uint.TryParse(parts[1], out uint mods))
        {
            return new Shortcut(vk, mods);
        }
        return null;
    }

    private static void SaveShortcut(Shortcut s, string key) => Set(key, $"{s.Key}:{s.Modifiers}");
}
