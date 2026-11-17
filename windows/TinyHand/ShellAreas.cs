using System;
using System.Collections.Generic;

namespace TinyHand;

/// Parts of the Windows shell where clicks should reach their target even while the hand is
/// on, like the menu bar and open menus on the Mac: taskbars, popup menus, and shell flyouts.
internal static class ShellAreas
{
    private static readonly string[] TaskbarClasses = { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private static readonly HashSet<string> MenuClasses = new(StringComparer.Ordinal)
    {
        "#32768",                               // classic popup menus (context menus, menu bars)
        "NotifyIconOverflowWindow",              // hidden tray icons, Windows 10
        "TopLevelWindowForOverflowXamlIsland",   // hidden tray icons, Windows 11
        "Xaml_WindowedPopupClass",               // XAML context menus (Windows 11 shell, modern apps)
        "Microsoft.UI.Content.PopupWindowSiteBridge", // WinUI 3 popups
    };

    // Start, search, Action Center and quick settings are foreground windows of these classes.
    private static readonly HashSet<string> ShellFlyoutClasses = new(StringComparer.Ordinal)
    {
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "ControlCenterWindow",
    };

    public static List<Native.RECT> Taskbars()
    {
        var list = new List<Native.RECT>();
        foreach (string cls in TaskbarClasses)
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = Native.FindWindowEx(IntPtr.Zero, hwnd, cls, null)) != IntPtr.Zero)
            {
                if (Native.IsWindowVisible(hwnd) && Native.GetWindowRect(hwnd, out Native.RECT r) && r.Width > 0 && r.Height > 0)
                {
                    list.Add(r);
                }
            }
        }
        return list;
    }

    public static List<Native.RECT> OpenMenus()
    {
        var list = new List<Native.RECT>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (Native.IsWindowVisible(hwnd) && MenuClasses.Contains(Native.ClassName(hwnd))
                && Native.GetWindowRect(hwnd, out Native.RECT r) && r.Width > 0 && r.Height > 0)
            {
                list.Add(r);
            }
            return true;
        }, IntPtr.Zero);

        IntPtr fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && ShellFlyoutClasses.Contains(Native.ClassName(fg))
            && Native.GetWindowRect(fg, out Native.RECT flyout) && flyout.Width > 0 && flyout.Height > 0)
        {
            list.Add(flyout);
        }
        return list;
    }
}
