# PC Stats Bar

A lightweight, TrafficMonitor-style stats overlay for the Windows 11 taskbar. It sits just left of the system tray and shows live hardware stats, each with its own coloured icon.

> **About this project:** I originally made this for my own personal use and decided to share it in case anyone else finds it useful. If you run into a bug or have an idea for a feature, please [open an issue](../../issues). I'll do my best to look into it and implement what I can.

## Features

**Stats** (each one can be turned on or off)

| Category | What it shows |
|---|---|
| Network | Upload speed, download speed |
| CPU | Usage, per-core usage bars, clock speed (GHz), temperature\*, package power\* |
| Memory | RAM usage %, RAM used / total (GB) |
| GPU (NVIDIA, AMD, Intel) | Usage, temperature, VRAM used / total, power draw. If you have more than one GPU, you can choose which one to show |
| Storage | Disk activity |
| Batteries | Laptop battery, plus Bluetooth headphones, mice, keyboards and controllers (with an icon for each device type) |

\* CPU temperature and power need the app to run as administrator (see below).

**Customisation**

- **Arrange page:** drag-and-drop or ▲ / ▼ buttons to set the left-to-right order, plus dividers to split stats into groups
- One-line or compact two-line layout
- Font and font-size picker (monospace fonts keep numbers from jumping around)
- Colour picker for every icon, the text, the warning colour and the background
- Rounded, see-through background with adjustable opacity
- Live preview inside the Settings window

**Quality of life**

- Matches light/dark taskbar automatically
- Warning colour when usage, temperature or battery levels hit their limits
- Hides itself in fullscreen games, videos and presentations
- Left-click opens Task Manager, and hovering shows details (CPU/GPU model, device names)
- Optional run at startup (as an elevated scheduled task when run as admin, so there's no UAC prompt at login)

## Download and use

1. Download `PCStatsBar.exe` from the [Releases](../../releases) page.
2. Run it. The bar appears on your taskbar next to the tray icons.
3. **Right-click** the bar and choose **Settings…** to customise it.

It's a single portable exe. There's no installer, and settings are saved in `HKCU\Software\PCStatsBar`.

### CPU temperature

Windows doesn't provide CPU temperatures to normal apps, so PC Stats Bar reads them through [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)'s library, which is built into the exe. This requires:

- **Administrator rights:** right-click the bar and choose **Restart as administrator**. To have it start elevated automatically, turn on **Run at startup** while it's running as admin.
- **The PawnIO driver:** it's installed along with the LibreHardwareMonitor app, or you can get it from [pawnio.eu](https://pawnio.eu).

## Requirements

- Windows 10 / 11 (64-bit)
- .NET Framework 4.7.2 or later (already included in Windows 10 and 11)
- An up-to-date graphics driver for GPU stats. NVIDIA cards are read through the driver's `nvml.dll`, and AMD / Intel cards through LibreHardwareMonitor. Neither needs admin rights.

### Choosing a GPU

On PCs with more than one GPU (for example, a desktop card plus the processor's built-in graphics), go to **Settings → Stats → Graphics card**. **Auto** picks the NVIDIA card if there is one, and otherwise the card with the most video memory.

## Known limitations

- Some stats aren't available on every GPU. Integrated graphics often don't report temperature or power, and these show as 0.
- Batteries of devices on proprietary USB dongles (Razer HyperSpeed, Logitech Lightspeed, SteelSeries, etc.) are only visible to their vendor apps, so they aren't shown. The same devices connected over Bluetooth do work.

## Building from source

No Visual Studio or SDK needed. It compiles with the .NET Framework compiler that's built into Windows.

1. Put the [LibreHardwareMonitorLib 0.9.6](https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6) NuGet package's .NET Framework DLLs (`runtimes/win-x64/lib/net472`), along with its dependencies' DLLs, into a `lib\` folder next to the source. The dependencies are DiskInfoToolkit, BlackSharp.Core, HidSharp, RAMSPDToolkit-NDD, System.Memory, System.Buffers, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe, System.CodeDom, System.Threading.AccessControl, System.Security.AccessControl and System.Security.Principal.Windows.
2. Run:

   ```bat
   build.bat
   ```

   This produces a single `PCStatsBar.exe`, with every DLL in `lib\` embedded inside it.

## Feedback

Found a bug or want a feature? [Open an issue](../../issues) and include your Windows version, CPU/GPU, and a screenshot if possible. Pull requests are welcome too.

## License

Released under the [MIT License](LICENSE). You're free to use, modify and share it, including commercially.

Third-party components bundled in the release exe keep their own licenses. [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) is licensed under MPL-2.0, and HidSharp under Apache-2.0.
