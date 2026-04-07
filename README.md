# TrayZoom

A lightweight Windows system-tray utility that gives you Mac-style smooth
full-screen zoom using a hotkey modifier + scroll wheel.  The view follows
your cursor as you move the mouse while zoomed in.

---

## Requirements

- Windows 8.1 or later (uses the Windows Magnification API)
- .NET 8 Runtime  →  https://dotnet.microsoft.com/download/dotnet/8.0
  (choose "Run desktop apps" → Windows x64)

> **Note:** TrayZoom runs without administrator rights. The scroll-wheel
> zoom hook works on all normal desktop windows. It will not intercept
> input over other processes running as administrator (e.g. Task Manager,
> UAC dialogs), but this rarely matters in practice.

---

## Quick start (pre-built binary)

1. Place `TrayZoom.exe` anywhere you like.
2. Double-click it to run.
3. A magnifier icon appears in the system tray.
4. Hold **Win** and scroll the mouse wheel to zoom in/out.
   The view centres on and follows your cursor.
5. Release the Win key; scroll normally at any time.

To exit, right-click the tray icon → **Exit**.
To reset zoom instantly, right-click → **Reset Zoom**.
To change settings, double-click the tray icon or right-click → **Settings…**

### Auto-starting with Windows

TrayZoom does not add itself to startup automatically.  To make it start
with Windows:

1. Press  Win + R  and type:
       shell:startup
2. Create a shortcut to TrayZoom.exe in the folder that opens.

---

## Command-line options

You can override the hotkey modifier and zoom speed for a single session
without touching the saved settings.  Useful for scripting or shortcuts.

    TrayZoom.exe [--modifier <value>] [--speed <value>] [--help]

### Options

  --modifier  -m  <Win|Ctrl|Alt|Ctrl+Alt>
      Which key to hold while scrolling to trigger zoom.
      Default: Win

  --speed  -s  <0.05 – 0.40>
      Animation smoothness.  Higher values are snappier.
      Default: 0.27

  --help  -h  /?
      Show a quick help dialog and exit.

### Examples

    TrayZoom.exe --modifier Ctrl --speed 0.30
    TrayZoom.exe -m Alt -s 0.15
    TrayZoom.exe -m Ctrl+Alt

Command-line values are session-only.  They do not overwrite the settings
saved via the GUI.

---

## Settings GUI

Double-click the tray icon (or right-click → Settings…) to open the
settings panel.

| Setting            | Description                                      | Default |
|--------------------|--------------------------------------------------|---------|
| Hotkey modifier    | Key to hold while scrolling                      | Win     |
| Zoom step per tick | How much each scroll notch zooms in/out          | 0.20×   |
| Maximum zoom       | Upper zoom limit                                 | 8×      |
| Smoothness         | Animation speed (Silky → Fast)                   | ~Fast   |

Click **Save** to persist settings to the registry under:
    HKCU\SOFTWARE\TrayZoom

---

## Building from source

### Prerequisites

- Windows 10 or 11
- .NET 8 SDK  →  https://dotnet.microsoft.com/download/dotnet/8.0
  (choose "Build apps" → SDK → Windows x64)

### Steps

1. Unzip the project folder.
2. Double-click **build.bat**
   — or open a terminal in the project folder and run:

       dotnet publish -c Release -r win-x64 --self-contained false ^
           -p:PublishSingleFile=true -o .\dist

3. The compiled `TrayZoom.exe` appears in the `dist\` folder.
4. Double-click it to run (see Quick start above).

### Project structure

    TrayZoom\
      Program.cs        — all application code (single file)
      TrayZoom.csproj   — .NET 8 WinForms project definition
      app.manifest      — requests admin elevation + DPI awareness
      build.bat         — one-click build script
      README.md         — this file

### Modifying the code

All logic lives in `Program.cs`.  Key areas:

- `Program.Main()`       — command-line argument parsing
- `TrayZoomApp`          — hook installation, zoom logic, tray menu
- `TrayZoomApp.SmoothTick()` — the 60 fps timer that lerps zoom and
                               repositions the view to follow the cursor
- `TrayZoomApp.ApplyZoom()`  — calls MagSetFullscreenTransform
- `SettingsForm`         — the dark-themed settings dialog

The default zoom speed is set by the `_smoothSpeed` field (0.27).
The default zoom step per scroll tick is `_zoomStep` (0.20).

---

## Troubleshooting

**Nothing happens when I scroll**
- Some security software blocks low-level input hooks; try adding an
  exception for TrayZoom.exe.
- The hook does not intercept input over windows running as administrator
  (e.g. Task Manager). This is a Windows limitation for non-elevated apps.

**"Could not initialise the Windows Magnification API"**
- Confirm you are on Windows 8.1 or later.
- Ensure magnification.dll is present in C:\Windows\System32.

**Zoom looks blurry**
- This is a limitation of the Windows Magnification API at very high
  zoom levels.  Try keeping the zoom below 4×.

**The tray icon is missing**
- It may be hidden.  Click the ^ arrow in the taskbar notification area
  to reveal hidden tray icons.

---

## Licence

Do whatever you like with this code.  No warranty is provided.
