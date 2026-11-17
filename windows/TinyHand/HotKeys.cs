using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace TinyHand;

/// System-wide hotkeys via RegisterHotKey, delivered to a hidden message window on the UI thread.
internal sealed class HotKeys : IDisposable
{
    public static HotKeys Shared { get; } = new();

    private readonly HwndSource window;
    private readonly Dictionary<int, Action> handlers = new();
    private int nextId = 1;

    private HotKeys()
    {
        var p = new HwndSourceParameters("TinyHand hotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        window = new HwndSource(p);
        window.AddHook(WndProc);
    }

    /// Returns an id, or null if another app already owns the combination.
    public int? Register(Shortcut shortcut, Action action) => Register(shortcut.Key, shortcut.Modifiers, action);

    public int? Register(uint key, uint modifiers, Action action)
    {
        int id = nextId++;
        if (!Native.RegisterHotKey(window.Handle, id, modifiers | Native.MOD_NOREPEAT, key))
        {
            return null;
        }
        handlers[id] = action;
        return id;
    }

    public void Unregister(int id)
    {
        Native.UnregisterHotKey(window.Handle, id);
        handlers.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && handlers.TryGetValue(wParam.ToInt32(), out Action? action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in handlers.Keys)
        {
            Native.UnregisterHotKey(window.Handle, id);
        }
        handlers.Clear();
        window.Dispose();
    }
}
