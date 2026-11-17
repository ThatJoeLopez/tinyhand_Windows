using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TinyHand;

/// Runs the pointer: a click-through window over the monitor under the cursor that draws the
/// hand, the hook that turns clicks into taps, the toolbar, and the hidden system cursor.
internal sealed class OverlayController
{
    private sealed class OverlayWindow : FloatingWindow
    {
        public OverlayWindow(ArmView arm)
        {
            Content = arm;
            ClickThrough = true;
            IsHitTestVisible = false;
            Title = "TinyHand pointer";
        }
    }

    private readonly Dispatcher ui;
    private readonly OverlayWindow window;
    private readonly ArmView armView = new();
    private readonly InputHook input;
    private readonly TapSound tapSound = new();
    private readonly Toolbar toolbar = new();

    private Native.RECT? currentFrame;
    private Native.RECT currentWork;
    private double currentScale = 1;
    private Point? downPoint;
    private int dragEvents;
    private bool interactHeld;
    private bool menuHover;
    private DateTime guardCursorUntil = DateTime.MinValue;
    private DateTime revealUntil = DateTime.MinValue;
    private bool normalCursor;
    private bool ownMenuOpen;
    private List<Native.RECT> menuRects = new();
    private DateTime lastMenuScan = DateTime.MinValue;
    private DateTime lastRaise = DateTime.MinValue;
    private int? escHotKey;

    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(50);
    private const double IdleAfter = 0.5;
    private const double MenuScanFast = 0.05;
    private const double MenuScanSlow = 0.25;
    private bool busy;
    private readonly DispatcherTimer idleTimer;
    private Native.POINT? lastMouse;
    private DateTime stillSince = DateTime.UtcNow;
    private DateTime lastButtonEvent = DateTime.MinValue;

    private DateTime? shiftDownAt;
    private uint shiftDownInputs;
    private DateTime lastShiftTap = DateTime.MinValue;
    private uint lastShiftTapInputs;
    private const double ShiftTapMax = 0.3;
    private const double ShiftDoubleMax = 0.35;

    public bool IsActive { get; private set; }

    public event Action? ActiveChanged;
    public event Action? SoundChanged;

    /// Extra areas, in physical pixels, where clicks reach the app as usual.
    public Func<IEnumerable<Native.RECT>>? ExtraPassThrough { get; set; }

    public OverlayController(Dispatcher ui)
    {
        this.ui = ui;
        window = new OverlayWindow(armView);
        input = new InputHook(ui);
        idleTimer = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = IdleInterval };
        idleTimer.Tick += (_, _) => Tick();

        input.MouseDown += () =>
        {
            armView.Press();
            armView.Held = true;
            tapSound.Play();
            toolbar.HideHint();
            downPoint = LocalMouse();
            dragEvents = 0;
            Log.Write($"press at {(downPoint is Point p ? $"{(int)p.X},{(int)p.Y}" : "?")}");
            NoteButtonEvent();
        };
        input.LeftDragged += () =>
        {
            dragEvents++;
            Drag();
            Wake();
        };
        input.MouseUp += () =>
        {
            Release();
            NoteButtonEvent();
        };
        input.InteractDown += () =>
        {
            armView.Press();
            armView.Held = true;
            NoteButtonEvent();
        };
        input.InteractUp += () =>
        {
            armView.Held = false;
            guardCursorUntil = DateTime.UtcNow.AddSeconds(2);
            NoteButtonEvent();
        };
        input.PassThroughPress += _ => NoteButtonEvent();
        input.Key += KeyChanged;
        input.Wake += Wake;

        toolbar.Pick += Pick;
        toolbar.ToggleSound += () => SoundOn = !SoundOn;
        toolbar.Exit += Exit;
        toolbar.Select(armView.Pointer);
        tapSound.Enabled = SoundOn;
        toolbar.SetSoundOn(SoundOn);

        Preferences.Changed += () =>
        {
            SyncSound();
            input.InteractVk = Preferences.InteractKey.VirtualKey();
            if (!Preferences.DoubleShiftClickThrough && IsClickThrough)
            {
                SetClickThrough(false);
            }
            armView.UpdateInkColor();
            tapSound.Load(Preferences.TapSound);
            if (armView.Pointer != PointerStyle.Saved)
            {
                Pick(PointerStyle.Saved);
            }
        };

        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            {
                ui.BeginInvoke(() => TurnOffForSystem($"session {e.Reason}"));
            }
        };
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Suspend)
            {
                ui.BeginInvoke(() => TurnOffForSystem("sleep"));
            }
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => ui.BeginInvoke(() =>
        {
            currentFrame = null;
            if (IsActive)
            {
                Tick();
            }
        });
    }

    public bool SoundOn
    {
        get => armView.Pointer.IsLaser ? Preferences.LaserSound : Preferences.HandSound;
        set
        {
            if (armView.Pointer.IsLaser)
            {
                Preferences.LaserSound = value;
            }
            else
            {
                Preferences.HandSound = value;
            }
        }
    }

    public PointerStyle Style => armView.Pointer;

    public bool IsClickThrough => IsActive && input.ClickThrough;

    public void Pick(PointerStyle style)
    {
        armView.SetStyle(style);
        PointerStyle.Saved = armView.Pointer;
        toolbar.Select(armView.Pointer);
        SyncSound();
    }

    private void SyncSound()
    {
        tapSound.Enabled = SoundOn;
        toolbar.SetSoundOn(SoundOn);
        SoundChanged?.Invoke();
    }

    /// Lets clicks through while TinyHand's own tray menu is open.
    public void SuspendInput(bool suspended)
    {
        if (!IsActive || !input.IsInstalled)
        {
            return;
        }
        ownMenuOpen = suspended;
        input.SetActive(!suspended);
        Tick();
    }

    public void Exit()
    {
        ClearInk();
        Deactivate();
    }

    public void ClearInk() => armView.Ink.Clear();

    public void Toggle()
    {
        if (IsActive)
        {
            Deactivate();
            return;
        }
        Activate();
    }

    public bool Activate()
    {
        if (IsActive)
        {
            return input.IsInstalled;
        }
        IsActive = true;
        currentFrame = null;
        tapSound.Warm();

        window.ShowQuietly();
        CursorHider.Hide();
        Tick();

        stillSince = DateTime.UtcNow;
        SetBusy(true);

        bool installed = input.Install();
        input.InteractVk = Preferences.InteractKey.VirtualKey();
        input.ClickThrough = false;
        input.SetActive(installed);
        Log.Write($"activate hookInstalled={installed}");

        toolbar.Show();
        if (currentFrame is Native.RECT f)
        {
            toolbar.Place(f, currentWork);
        }
        revealUntil = DateTime.UtcNow.AddSeconds(3);
        if (!installed)
        {
            toolbar.ShowHint(new[] { ("Clicks can't be captured", "taps and drawing are off") });
            revealUntil = DateTime.UtcNow.AddSeconds(10);
        }
        else if (!Preferences.FlagSet("firstRunHintShown"))
        {
            Preferences.SetFlag("firstRunHintShown");
            var parts = new List<(string, string)> { ("Click", "to tap"), ("Drag", "to draw") };
            if (Preferences.InteractKey != InteractKey.Off)
            {
                parts.Add(($"Hold {Preferences.InteractKey.Title()}", "to click for real"));
            }
            if (Preferences.DoubleShiftClickThrough)
            {
                parts.Add(("Double-tap Shift", "to click through"));
            }
            parts.Add(("Esc", "to exit"));
            toolbar.ShowHint(parts);
            toolbar.HideHint(TimeSpan.FromSeconds(10));
            revealUntil = DateTime.UtcNow.AddSeconds(10);
        }
        SetEscEnabled(true);
        ActiveChanged?.Invoke();
        return installed;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }
        IsActive = false;

        SetBusy(false);
        idleTimer.Stop();
        input.WakeOnInput = false;
        input.SetActive(false);
        input.ClickThrough = false;
        input.Dispose();
        armView.Held = false;
        downPoint = null;
        armView.Ink.Clear();
        window.Hide();
        toolbar.Hide();
        normalCursor = false;
        armView.PointerHidden = false;
        input.PassThroughRects = Array.Empty<Native.RECT>();
        SetEscEnabled(false);
        ownMenuOpen = false;
        shiftDownAt = null;
        CursorHider.Show();
        ActiveChanged?.Invoke();
    }

    private void TurnOffForSystem(string reason)
    {
        if (!IsActive)
        {
            return;
        }
        Log.Write($"turning pointer off: {reason}");
        Deactivate();
    }

    private void KeyChanged(int vk, bool down, uint inputs)
    {
        if (vk is Native.VK_CONTROL or 0xA2 or 0xA3 && armView.Ink.IsDrawing)
        {
            armView.Ink.Arrow = WantsArrow();
        }
        if (!Preferences.DoubleShiftClickThrough)
        {
            return;
        }
        bool isShift = vk is Native.VK_SHIFT or Native.VK_LSHIFT or Native.VK_RSHIFT;
        bool otherModifier = Native.KeyDown(Native.VK_CONTROL) || Native.KeyDown(Native.VK_MENU)
            || Native.KeyDown(Native.VK_LWIN) || Native.KeyDown(Native.VK_RWIN);
        if (isShift && down)
        {
            if (otherModifier)
            {
                shiftDownAt = null;
                lastShiftTap = DateTime.MinValue;
            }
            else if (shiftDownAt == null)
            {
                shiftDownAt = DateTime.UtcNow;
                shiftDownInputs = inputs;
            }
            return;
        }
        if (!isShift)
        {
            if (down)
            {
                shiftDownAt = null;
                lastShiftTap = DateTime.MinValue;
            }
            return;
        }
        // Shift released.
        if (shiftDownAt is not DateTime pressed)
        {
            return;
        }
        shiftDownAt = null;
        DateTime now = DateTime.UtcNow;
        if ((now - pressed).TotalSeconds >= ShiftTapMax || inputs != shiftDownInputs || otherModifier)
        {
            lastShiftTap = DateTime.MinValue;
            return;
        }
        bool isDouble = (pressed - lastShiftTap).TotalSeconds < ShiftDoubleMax && shiftDownInputs == lastShiftTapInputs;
        if (isDouble)
        {
            lastShiftTap = DateTime.MinValue;
            SetClickThrough(!input.ClickThrough);
            return;
        }
        lastShiftTap = now;
        lastShiftTapInputs = inputs;
    }

    private void SetClickThrough(bool on)
    {
        if (!IsActive)
        {
            return;
        }
        input.ClickThrough = on;
        Log.Write(on ? "click-through on" : "click-through off");
        if (on)
        {
            Release();
        }
        toolbar.ShowHint(on
            ? new[] { ("Clicks go through", "the hand stays"), ("Double-tap Shift", "to tap again") }
            : new[] { ("Taps", "clicks don't reach apps"), ("Double-tap Shift", "to click through") });
        toolbar.HideHint(TimeSpan.FromSeconds(1.5));
        Wake();
        ActiveChanged?.Invoke();
    }

    private void Release()
    {
        if (downPoint is Point start)
        {
            int moved = LocalMouse() is Point p ? (int)(p - start).Length : -1;
            Log.Write($"release, drag events={dragEvents} moved={moved}px stroke drawn={armView.Ink.IsDrawing}");
        }
        armView.Held = false;
        downPoint = null;
        armView.Ink.End();
    }

    private void Drag()
    {
        if (downPoint is not Point start || LocalMouse() is not Point p)
        {
            return;
        }
        Ink ink = armView.Ink;
        if (!ink.IsDrawing)
        {
            if ((p - start).Length < 4)
            {
                return;
            }
            ink.Begin(start, armView.InkWidth);
        }
        ink.Arrow = WantsArrow();
        ink.Extend(p);
    }

    /// Ctrl while drawing adds an arrowhead (Command on the Mac).
    private static bool WantsArrow() => Native.KeyDown(Native.VK_CONTROL);

    private Point? LocalMouse()
    {
        if (currentFrame is not Native.RECT f)
        {
            return null;
        }
        Native.GetCursorPos(out Native.POINT m);
        return new Point((m.X - f.Left) / currentScale, (m.Y - f.Top) / currentScale);
    }

    // Diagnostics for the self-test.
    public int InkStrokes => armView.Ink.StrokeCount;
    public string DebugState => $"held={armView.Held} downPoint={downPoint != null} drawing={armView.Ink.IsDrawing} strokes={InkStrokes}";
    public Point? ToolbarButtonCenter(PointerStyle style) => toolbar.CenterOf(style);

    private void UpdateZones(Native.POINT mouse)
    {
        DateTime now = DateTime.UtcNow;
        Native.RECT f = toolbar.Frame;
        int pad = (int)(80 * currentScale);
        var near = new Native.RECT { Left = f.Left - pad, Right = f.Right + pad, Top = f.Top - (int)(100 * currentScale), Bottom = f.Bottom + (int)(10 * currentScale) };
        bool drawing = armView.Ink.IsDrawing;
        toolbar.SetRevealed(Preferences.ShowToolbar && !drawing && (near.Contains(mouse) || now < revealUntil));

        bool menuLikely = menuRects.Count > 0 || ownMenuOpen || (now - lastButtonEvent).TotalSeconds < 2;
        double scanEvery = menuLikely ? MenuScanFast : MenuScanSlow;
        if ((now - lastMenuScan).TotalSeconds > scanEvery)
        {
            lastMenuScan = now;
            menuRects = ShellAreas.OpenMenus();
        }
        List<Native.RECT> taskbars = ShellAreas.Taskbars();
        bool overTaskbar = taskbars.Exists(r => r.Contains(mouse));
        bool overMenu = overTaskbar || menuRects.Exists(r => r.Contains(mouse));

        int key = Preferences.InteractKey.VirtualKey();
        bool interacting = (key != 0 && Native.KeyDown(key)) || input.InteractPressing;
        interactHeld = interacting;
        menuHover = overMenu;
        bool through = input.ClickThrough;
        bool normal = !drawing && !Preferences.InteractKeepsHand && ((interacting && !through) || overMenu || ownMenuOpen);
        SetEscEnabled(menuRects.Count == 0 && !ownMenuOpen && !through);

        if (normal != normalCursor)
        {
            string why = interacting ? "interact key" : overTaskbar ? "taskbar" : "menu";
            Log.Write(normal ? $"real cursor ({why}) at {mouse.X},{mouse.Y}" : $"pointer back at {mouse.X},{mouse.Y}");
            normalCursor = normal;
            armView.PointerHidden = normal;
            if (normal)
            {
                CursorHider.Show();
            }
            else
            {
                CursorHider.Hide();
            }
        }

        var rects = new List<Native.RECT>();
        if (ExtraPassThrough != null)
        {
            rects.AddRange(ExtraPassThrough());
        }
        rects.AddRange(taskbars);
        rects.AddRange(menuRects);
        if (toolbar.Revealed)
        {
            rects.Add(f);
        }
        input.PassThroughRects = rects.ToArray();
    }

    private void SetEscEnabled(bool enabled)
    {
        if (enabled && IsActive && escHotKey == null)
        {
            escHotKey = HotKeys.Shared.Register(Native.VK_ESCAPE, 0, Exit);
        }
        if (!enabled && escHotKey is int id)
        {
            HotKeys.Shared.Unregister(id);
            escHotKey = null;
        }
    }

    private void NoteButtonEvent()
    {
        lastButtonEvent = DateTime.UtcNow;
        Wake();
    }

    private void Wake()
    {
        stillSince = DateTime.UtcNow;
        if (!IsActive || busy)
        {
            return;
        }
        SetBusy(true);
        Tick();
    }

    /// Busy: redraw every frame with the compositor. Idle: a slow timer, woken by the hook.
    private void SetBusy(bool value)
    {
        if (value == busy)
        {
            return;
        }
        busy = value;
        if (value)
        {
            idleTimer.Stop();
            input.WakeOnInput = false;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            if (IsActive)
            {
                input.WakeOnInput = true;
                idleTimer.Start();
            }
        }
    }

    private void OnRendering(object? sender, EventArgs e) => Tick();

    private void UpdatePace(bool keepBusy)
    {
        if (!busy)
        {
            if (keepBusy)
            {
                Wake();
            }
            return;
        }
        bool still = (DateTime.UtcNow - stillSince).TotalSeconds > IdleAfter;
        if (still && !keepBusy && !armView.Held && !armView.IsSettling && !armView.Ink.IsDrawing)
        {
            SetBusy(false);
        }
    }

    private void Tick()
    {
        if (!IsActive)
        {
            return;
        }
        Native.GetCursorPos(out Native.POINT mouse);
        (Native.RECT bounds, Native.RECT work, double scale, _) = Native.MonitorAt(mouse);

        if (currentFrame is not Native.RECT frame || !frame.Equals(bounds) || Math.Abs(scale - currentScale) > 0.001)
        {
            bool moving = currentFrame != null;
            currentFrame = bounds;
            currentWork = work;
            currentScale = scale;
            if (moving)
            {
                Release();
                revealUntil = DateTime.UtcNow.AddSeconds(1.5);
            }
            armView.Ink.Clear();
            MoveOverlay(bounds, scale);
            toolbar.Place(bounds, work);
        }

        if (lastMouse is not Native.POINT last || last.X != mouse.X || last.Y != mouse.Y)
        {
            lastMouse = mouse;
            stillSince = DateTime.UtcNow;
        }

        UpdateZones(mouse);
        bool urgent = interactHeld || menuHover || DateTime.UtcNow < guardCursorUntil;

        // The taskbar and other topmost windows can come in front when clicked; stay above them.
        if ((DateTime.UtcNow - lastRaise).TotalSeconds > 0.5)
        {
            lastRaise = DateTime.UtcNow;
            toolbar.RaiseToTop();
            window.RaiseToTop();
        }

        var local = new Point((mouse.X - bounds.Left) / scale, (mouse.Y - bounds.Top) / scale);
        armView.Aim(local);
        if (downPoint == null)
        {
            UpdatePace(keepBusy: (!normalCursor && urgent) || CursorHider.IsSettling);
            return;
        }
        Drag();
    }

    private void MoveOverlay(Native.RECT bounds, double scale)
    {
        armView.Scale = scale;
        armView.SetScreenSize(bounds.Width / scale, bounds.Height / scale);
        window.Width = bounds.Width / scale;
        window.Height = bounds.Height / scale;
        Place();
        // Moving onto a monitor with another scale factor makes WPF resize the window for the
        // new DPI; put it back once that has happened.
        ui.BeginInvoke(DispatcherPriority.Loaded, Place);

        void Place()
        {
            Native.SetWindowPos(window.Handle, Native.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
    }

    /// Times the hot paths, for `TinyHand.exe --click-test`.
    public string RunClickTest()
    {
        var log = new System.Text.StringBuilder();
        void Time(string label, Action body)
        {
            var sw = Stopwatch.StartNew();
            body();
            log.AppendLine($"{label}: {sw.Elapsed.TotalMilliseconds:F1} ms");
        }
        IsActive = true;
        currentFrame = null;
        window.ShowQuietly();
        Tick();
        Time("tick", Tick);
        Time("mouseDown (hook callback)", () => input.Simulate(Native.WM_LBUTTONDOWN));
        Time("mouseDown (deferred work)", () => Pump(10));
        Time("10 ticks while held", () =>
        {
            for (int i = 0; i < 10; i++) Tick();
        });
        Time("mouseUp (hook callback)", () => input.Simulate(Native.WM_LBUTTONUP));
        Pump(10);
        Time("drag x50 (hook callbacks)", () =>
        {
            input.Simulate(Native.WM_LBUTTONDOWN);
            for (int i = 0; i < 50; i++) input.Simulate(Native.WM_MOUSEMOVE);
            input.Simulate(Native.WM_LBUTTONUP);
        });
        Pump(50);
        Time("20 ticks after up", () =>
        {
            for (int i = 0; i < 20; i++) Tick();
        });
        log.AppendLine(DebugState);
        IsActive = false;
        window.Hide();
        return log.ToString();
    }

    private void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
