# PC Stats Bar v1.0.0

The first public release of PC Stats Bar: a lightweight, TrafficMonitor-style stats overlay that sits on your Windows 11 taskbar, just left of the system tray.

I originally built this for my own PC and decided to share it. If you find a bug or have a feature idea, please [open an issue](../../issues) and I'll do my best to add it.

## Download

Download **`PCStatsBar.exe`** below and run it. It's a single portable file, with no installer. Right-click the bar and choose **Settings…** to customise it.

## Features

### Live stats, each with its own icon
- **Network:** upload and download speed
- **CPU:** usage, per-core usage bars, clock speed, temperature\*, package power\*
- **Memory:** RAM usage %, used / total GB
- **GPU:** usage, temperature, VRAM, power draw for **NVIDIA, AMD and Intel** cards, with a picker for PCs with more than one GPU
- **Storage:** disk activity
- **Batteries:** laptop battery, plus Bluetooth headphones, mice, keyboards and controllers, each with an icon for its device type

\* Needs the app to run as administrator (right-click the bar → **Restart as administrator**).

### Customisation
- **Arrange page:** drag-and-drop or ▲ / ▼ buttons to reorder stats, plus dividers to group them
- One-line or compact two-line layout
- Font and size picker
- Colour picker for every icon, the text, the warning colour and the background
- Rounded, see-through background with adjustable opacity
- Live preview in a dark-themed Settings window

### Quality of life
- Automatically matches a light or dark taskbar
- Values turn red when usage or temperature gets high, or a battery gets low
- Hides in fullscreen games, videos and presentations
- Left-click opens Task Manager, and hovering shows CPU/GPU models and device names
- Optional run at startup, which uses an elevated scheduled task when run as admin so there's no UAC prompt at login

## Requirements
- Windows 10 / 11 (64-bit)
- .NET Framework 4.7.2+ (already included in Windows 10 and 11)
- For CPU temperature: admin rights and the PawnIO driver (installed with LibreHardwareMonitor, or from [pawnio.eu](https://pawnio.eu))

## Known limitations
- Batteries of devices on proprietary USB dongles (Razer HyperSpeed, Logitech Lightspeed, SteelSeries, etc.) are only visible to their vendor apps, so they aren't shown. The same devices connected over Bluetooth work.
- Integrated graphics often don't report temperature or power.
- Windows SmartScreen may warn you because the exe isn't code-signed. Click **More info → Run anyway**.

## Credits
CPU and AMD/Intel GPU sensors are powered by [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0), which is bundled inside the exe.
