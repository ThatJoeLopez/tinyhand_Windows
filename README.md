<img src="./.github/logo.png" width="80" />

# TinyHand for Windows

[![Windows build](https://github.com/ThatJoeLopez/tinyhand_Windows/actions/workflows/windows.yml/badge.svg)](https://github.com/ThatJoeLopez/tinyhand_Windows/actions/workflows/windows.yml)
[![Platform: Windows 10 | 11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?logo=windows)](#install)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)

A tiny hand on a stick that points at things on your Windows screen. It's great for demos,
screen recordings, presentations and teaching.

This is a Windows port of [TinyHand](https://github.com/nilbuild/tinyhand) by
[@nilbuild](https://github.com/nilbuild). It uses the same hands, sounds and behaviour as the
original Mac app, rewritten for Windows in C# and WPF.

<img src="./.github/banner.jpg" alt="TinyHand: a tiny toy hand on a stick pointing at the screen" />

[![Download for Windows](https://img.shields.io/badge/Download_for_Windows-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/ThatJoeLopez/tinyhand_Windows/releases/latest/download/TinyHand-win-x64.zip)
[![Download for Windows on Arm](https://img.shields.io/badge/Windows_on_Arm-333333?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/ThatJoeLopez/tinyhand_Windows/releases/latest/download/TinyHand-win-arm64.zip)

Windows 10 (1809 or later) and Windows 11, on x64 and Arm PCs.

## Install

1. Download `TinyHand-win-x64.zip` from the button above, or from
   [Releases](https://github.com/ThatJoeLopez/tinyhand_Windows/releases). Get
   `TinyHand-win-arm64.zip` instead if your PC has an Arm chip (check **Settings → System →
   About → System type**).
2. Right-click the zip, choose **Extract All…**, and put the `TinyHand` folder somewhere you'll
   keep it, for example `%LOCALAPPDATA%\Programs\TinyHand`.
3. Run `TinyHand.exe`. Keep it next to its `assets` folder.

You don't need an installer, admin rights, the .NET runtime or a permission prompt. The app
isn't code-signed yet, so SmartScreen may warn you the first time. Choose
**More info → Run anyway**.

TinyHand lives in the notification area at the right of the taskbar. If you don't see the hand
icon, click **^** to show hidden icons.

## Use

| | |
|---|---|
| Click the tray icon, or **Ctrl+Alt+H** | Turn the pointer on or off |
| Right-click the tray icon | Menu and Settings |
| Click | Tap (nothing underneath gets clicked) |
| Hold and drag | Draw on the screen |
| Hold **Ctrl** while drawing | Add an arrowhead to the end of the line |
| Hold **Alt** and click | Click for real |
| Double-tap **Shift** | Let every click through while the hand stays (the icon turns orange) |
| **Ctrl+Alt+X** | Clear the drawing |
| **Esc** | Clear everything and turn the pointer off |

While the pointer is on, a small toolbar appears when you move near the bottom of the screen.
Use it to pick one of 36 hands or a laser dot, change the ink colour, mute the tap sound, or
exit. The taskbar, Start menu and right-click menus keep working normally.

In **Settings** (right-click the tray icon) you can change the shortcuts, choose which pointers
are in the toolbar, and set the size, shadow, ink colour, fade time and tap sound. You can also
make your own hand from any PNG with a transparent background.

## Automation

Launchers like PowerToys Run or Flow Launcher, and scripts, can control TinyHand with
`tinyhand://` links:

```bat
start tinyhand://toggle
```

| Link | Action |
|---|---|
| `tinyhand://on`, `tinyhand://off`, `tinyhand://toggle` | Turn the pointer on, off, or flip it |
| `tinyhand://clear` | Clear the drawing |
| `tinyhand://style/<id>` | Switch to a hand (or `laser`) and turn the pointer on |
| `tinyhand://sound/on`, `/off`, `/toggle` | Tap sound for the current pointer |

While TinyHand runs, it keeps its current state in `%APPDATA%\TinyHand\state.json`. The file
has whether the pointer is on, the selected style, whether sound is on, the app's `pid`, and
every style with its `id`, name and type. It's removed when TinyHand quits.

More detail is in [windows/README.md](windows/README.md): where settings live, building from
source, how the port works, and known gaps.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows:

```bat
dotnet run --project windows\TinyHand
```

## Looking for the Mac app?

The original macOS version is at [nilbuild/tinyhand](https://github.com/nilbuild/tinyhand) and
[tinyhand.app](https://tinyhand.app). Its Swift source is still in this repository under
`Sources/`.

## License

MIT, like the original. See [LICENSE](LICENSE).
