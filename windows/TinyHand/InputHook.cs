using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace TinyHand;

/// Low-level mouse and keyboard hooks on their own thread.
///
/// While active, mouse button presses are swallowed (so nothing underneath gets clicked) and
/// reported as taps, unless the interact key is held, click-through is latched, or the press
/// lands in a pass-through area such as the taskbar or TinyHand's toolbar. Moves always pass,
/// so the cursor keeps tracking. The hook thread does no UI work: Windows unhooks a low-level
/// hook that answers too slowly, so every callback is posted to the UI dispatcher.
internal sealed class InputHook : IDisposable
{
    public event Action? MouseDown;
    public event Action? MouseUp;
    public event Action? LeftDragged;
    public event Action? InteractDown;
    public event Action? InteractUp;
    public event Action<Native.POINT>? PassThroughPress;
    /// Every key press and release, for double-Shift detection: (vk, down, inputCount).
    public event Action<int, bool, uint>? Key;
    /// Raised once for the next mouse event after WakeOnInput is set.
    public event Action? Wake;

    /// Set while the pointer idles, so the next mouse event speeds the overlay back up.
    public volatile bool WakeOnInput;

    private readonly Dispatcher ui;
    private readonly object gate = new();
    private bool active;
    private Native.RECT[] passThroughRects = Array.Empty<Native.RECT>();
    private int interactVk;
    private bool interactPress;
    private bool clickThrough;
    private bool passingThrough;
    private bool leftHeld;
    private uint inputCount;

    private Thread? thread;
    private uint threadId;
    private IntPtr mouseHook;
    private IntPtr keyboardHook;
    // Kept alive for as long as the hooks are installed.
    private Native.HookProc? mouseProc;
    private Native.HookProc? keyboardProc;

    public bool IsInstalled { get; private set; }

    public InputHook(Dispatcher ui)
    {
        this.ui = ui;
    }

    public Native.RECT[] PassThroughRects
    {
        get { lock (gate) return passThroughRects; }
        set { lock (gate) passThroughRects = value; }
    }

    public int InteractVk
    {
        get { lock (gate) return interactVk; }
        set { lock (gate) interactVk = value; }
    }

    /// Latched by double-tapping Shift: every click reaches the app, as if the interact key were held.
    public bool ClickThrough
    {
        get { lock (gate) return clickThrough; }
        set { lock (gate) clickThrough = value; }
    }

    public bool InteractPressing
    {
        get { lock (gate) return interactPress; }
    }

    /// Key presses and clicks seen since the hooks were installed.
    public uint InputCount
    {
        get { lock (gate) return inputCount; }
    }

    public void SetActive(bool value)
    {
        lock (gate)
        {
            active = value;
            interactPress = false;
            passingThrough = false;
            leftHeld = false;
        }
    }

    public bool Install()
    {
        if (IsInstalled)
        {
            return true;
        }
        using var ready = new ManualResetEventSlim();
        bool ok = false;
        thread = new Thread(() =>
        {
            threadId = Native.GetCurrentThreadId();
            mouseProc = MouseCallback;
            keyboardProc = KeyboardCallback;
            IntPtr module = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
            keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, module, 0);
            ok = mouseHook != IntPtr.Zero;
            if (!ok)
            {
                Log.Write($"mouse hook failed: error {Marshal.GetLastWin32Error()}");
            }
            ready.Set();
            if (!ok)
            {
                return;
            }
            while (Native.GetMessage(out Native.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            Native.UnhookWindowsHookEx(mouseHook);
            if (keyboardHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(keyboardHook);
            }
            mouseHook = IntPtr.Zero;
            keyboardHook = IntPtr.Zero;
        })
        {
            Name = "TinyHand input hook",
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
        thread.Start();
        ready.Wait();
        IsInstalled = ok;
        return ok;
    }

    public void Dispose()
    {
        if (thread != null && threadId != 0)
        {
            Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(500);
        }
        thread = null;
        IsInstalled = false;
    }

    private void Post(Action? action)
    {
        if (action != null)
        {
            ui.BeginInvoke(action);
        }
    }

    private static bool IsDown(int msg) => msg is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN;

    private static bool IsUp(int msg) => msg is Native.WM_LBUTTONUP or Native.WM_RBUTTONUP or Native.WM_MBUTTONUP or Native.WM_XBUTTONUP;

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }
        int msg = wParam.ToInt32();
        var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
        bool swallow = Handle(msg, info);
        return swallow ? new IntPtr(1) : Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    /// Returns true when the event should not reach any app.
    private bool Handle(int msg, Native.MSLLHOOKSTRUCT info)
    {
        bool down = IsDown(msg);
        bool up = IsUp(msg);
        if (WakeOnInput)
        {
            WakeOnInput = false;
            Post(Wake);
        }
        int key;
        lock (gate)
        {
            if (down)
            {
                inputCount++;
            }
            if (!active)
            {
                return false;
            }
            key = interactVk;
        }

        if (down)
        {
            bool viaKey = key != 0 && Native.KeyDown(key);
            if (viaKey || ClickThrough)
            {
                lock (gate) interactPress = true;
                if (viaKey && key == Native.VK_MENU)
                {
                    MaskAltMenu();
                }
                Post(InteractDown);
            }
        }
        bool pressing;
        lock (gate) pressing = interactPress;
        if (pressing)
        {
            if (up)
            {
                lock (gate) interactPress = false;
                Post(InteractUp);
            }
            return false;
        }

        if (down)
        {
            bool inside = false;
            foreach (Native.RECT r in PassThroughRects)
            {
                if (r.Contains(info.pt))
                {
                    inside = true;
                    break;
                }
            }
            passingThrough = inside;
            if (inside)
            {
                Native.POINT at = info.pt;
                ui.BeginInvoke(() => PassThroughPress?.Invoke(at));
            }
        }
        if (passingThrough)
        {
            if (up)
            {
                passingThrough = false;
            }
            return false;
        }

        if (down)
        {
            if (msg == Native.WM_LBUTTONDOWN)
            {
                leftHeld = true;
            }
            Post(MouseDown);
            return true;
        }
        if (up)
        {
            if (msg == Native.WM_LBUTTONUP)
            {
                leftHeld = false;
            }
            Post(MouseUp);
            return true;
        }
        if (msg == Native.WM_MOUSEMOVE && leftHeld)
        {
            Post(LeftDragged);
        }
        return false;
    }

    /// Releasing Alt on its own opens the menu bar of the focused app. A dummy key press while
    /// Alt is down tells Windows the Alt press was used for something else.
    private static void MaskAltMenu()
    {
        var inputs = new Native.INPUT[2];
        inputs[0].type = Native.INPUT_KEYBOARD;
        inputs[0].ki.wVk = 0xE8;
        inputs[1].type = Native.INPUT_KEYBOARD;
        inputs[1].ki.wVk = 0xE8;
        inputs[1].ki.dwFlags = Native.KEYEVENTF_KEYUP;
        Native.SendInput(2, inputs, Marshal.SizeOf<Native.INPUT>());
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            int vk = (int)info.vkCode;
            uint count;
            bool on;
            lock (gate)
            {
                if (down && vk != 0xE8 && !IsModifier(vk))
                {
                    inputCount++;
                }
                count = inputCount;
                on = active;
            }
            if (on && (down || up) && vk != 0xE8)
            {
                ui.BeginInvoke(() => Key?.Invoke(vk, down, count));
            }
        }
        return Native.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsModifier(int vk) => vk is (>= 0x10 and <= 0x12) or (>= 0xA0 and <= 0xA5) or Native.VK_LWIN or Native.VK_RWIN;

    /// Runs a fake event through the hook logic, for the self-test.
    public void Simulate(int msg)
    {
        bool was;
        lock (gate)
        {
            was = active;
            active = true;
        }
        Handle(msg, new Native.MSLLHOOKSTRUCT());
        lock (gate) active = was;
    }
}
