# TinyHand for Windows

A port of [TinyHand](https://github.com/nilbuild/tinyhand) to Windows 10 and 11: a tiny hand on a
stick that points at things on your screen. It uses the same hands, sounds and behaviour as the
Mac app, rewritten in C# with WPF.

## Install

Download `TinyHand-win-x64.zip` (or `-win-arm64` for Arm PCs) from the
[Actions artifacts or Releases](../../../actions/workflows/windows.yml), unzip it anywhere (for
example `%LOCALAPPDATA%\Programs\TinyHand`) and run `TinyHand.exe`. No admin rights, installer
or extra runtime are needed, and no permission prompt: Windows lets TinyHand see your clicks
without one.

The build isn't code-signed, so SmartScreen may warn the first time. Choose **More info → Run anyway**.

## Use

| | |
|---|---|
| Click the hand in the notification area, or **Ctrl+Alt+H** | Turn the pointer on or off |
| Right-click the notification-area icon | Menu and Settings |
| Click | Tap (nothing underneath gets clicked) |
| Hold and drag | Draw on the screen |
| Hold **Ctrl** while drawing | Add an arrowhead to the end of the line |
| Hold **Alt** and click | Click for real (configurable) |
| Double-tap **Shift** | Let all clicks through while the hand stays (the icon turns orange) |
| **Ctrl+Alt+X** | Clear the drawing |
| **Esc** | Clear everything and turn the pointer off |

While the pointer is on, a toolbar appears when you move near the bottom of the screen. Pick one
of 36 hands or a laser dot, change the ink colour, mute the tap sound, or exit. The taskbar,
open menus and the Start menu keep working normally while the hand is out.

Settings (right-click the tray icon, or launch `TinyHand.exe` again) has shortcuts, the toolbar
line-up, size, shadow, ink, fade time and sounds. You can also make your own hand from any PNG
with a transparent background: **Settings → Pointers → Make your own…**.

## Automation

Launchers like PowerToys Run, Flow Launcher or a script can drive TinyHand with `tinyhand://`
links, registered for the current user when TinyHand starts:

```bat
start tinyhand://toggle
```

| Link | Action |
|---|---|
| `tinyhand://on`, `tinyhand://off`, `tinyhand://toggle` | Turn the pointer on, off, or flip it |
| `tinyhand://clear` | Clear the drawing |
| `tinyhand://style/<id>` | Switch to a hand (or `laser`) and turn the pointer on |
| `tinyhand://sound/on`, `/off`, `/toggle` | Tap sound for the current pointer |

`TinyHand.exe tinyhand://toggle` works too. While TinyHand runs it keeps its state in
`%APPDATA%\TinyHand\state.json` (same fields as the Mac app: `active`, `style`, `soundOn`,
`pid` and every style's `id`, `name` and `type`), and removes it on quit.

## Where things live

| | |
|---|---|
| Settings | `%APPDATA%\TinyHand\settings.json` |
| Your own hands | `%APPDATA%\TinyHand\Hands\` |
| Log | `%LOCALAPPDATA%\TinyHand\TinyHand.log` |
| Open at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TinyHand` |

## Build

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```bat
dotnet run --project windows\TinyHand
```

A self-contained build, like the one CI makes:

```bat
dotnet publish windows\TinyHand -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out\TinyHand
```

The hand images and sounds come from the shared `assets` folder at the repository root and are
copied next to the exe.

## How it works, compared with the Mac app

| Mac | Windows |
|---|---|
| Accessibility event tap swallows clicks | Low-level mouse hook (`WH_MOUSE_LL`) on its own thread |
| Menu bar and open menus pass clicks through | Taskbars, popup menus, tray overflow and shell flyouts pass clicks through |
| `CGDisplayHideCursor` | System cursors swapped for blank ones, restored with `SPI_SETCURSORS` (and on the next launch after a crash) |
| Carbon hotkeys | `RegisterHotKey` |
| Option to click for real, Command for arrows | Alt to click for real, Ctrl for arrows |
| Sparkle updates | Not included yet: download new builds from Releases |
| Login item | `Run` registry key for the current user |

Known gaps: no auto-update, the toolbar can't be reordered by dragging (use the arrows in
Settings), apps that draw their own custom cursor images may still show them while the hand
is out, and over windows of apps running as administrator clicks go through, because Windows
doesn't let a normal app intercept input meant for them.
