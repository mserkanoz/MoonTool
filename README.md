# MoonTool

A lightweight Windows system tray app that displays the current moon phase.

![Moon phases](moon.ico)

## Features

- System tray icon that updates every 5 minutes to reflect the current moon phase
- Click the tray icon to open a details panel showing:
  - Current phase name and illumination percentage
  - Moon's age, distance from Earth, and angular diameter
  - Sun's distance and angular diameter
  - Local time, UTC, and Julian date
  - Dates of the last and next new moon, first quarter, full moon, and last quarter
- Southern hemisphere view toggle (mirrors the phase display)
- Optional launch at Windows startup
- Single instance — only one copy runs at a time

> **Note:** The UI is in Turkish. Phase names and labels are displayed in Turkish.

## Requirements

- Windows 10 or later
- .NET Framework 4.x (included with Windows 10)

## Download

Grab the latest `MoonTool.exe` from the [Releases](../../releases) page and run it — no installation needed.

## Build from source

The app is a single `.cs` file. You can compile it using the C# compiler that ships with Windows:

```
csc /target:winexe /out:MoonTool.exe ^
    /r:System.Windows.Forms.dll ^
    /r:System.Drawing.dll ^
    /win32icon:moon.ico ^
    /win32manifest:app.manifest ^
    MoonTool.cs
```

`csc.exe` is typically found at:
```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

## Credits

Moon phase calculation is adapted from [John Walker's](https://www.fourmilab.ch/) classic public-domain MoonTool algorithm.

## License

MIT
