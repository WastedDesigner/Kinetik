# Kinetik v2.0.0 (formerly PC Stats Bar)

- **New name and logo: PC Stats Bar is now Kinetik.** The new logo is rising stat bars inside a spinning ring. The exe is now `Kinetik.exe`.
  - Your settings are copied over automatically the first time Kinetik runs
  - If you had **Run at startup** on, start Kinetik once as administrator (right-click → **Restart as administrator**) so it can swap the old startup entry for the new one
- **New: spinning tray icon.** The ring in the tray logo turns faster the busier your CPU is. Turn it off in **Settings → General → Spinning tray icon**.
- **New: animated icons** on the taskbar bar and the desktop widget. Every icon acts out what it shows, and moves faster or harder the busier its stat is:
  - Fans spin with fan speed (with motion blur when fast), and the graphics-card icon's fan spins with GPU load
  - Upload/download arrows stream in their direction; drive arrows flow in and out while the activity LED flickers
  - Thermometers fill up and turn redder as things heat up, with heat shimmer when hot; flames flicker and throw sparks
  - Power bolts crackle with little electric arcs; the CPU chip's pins light up in a chase and its core glows under load
  - The gauge needle swings to the current load, RAM chips blink like LEDs, VRAM cells twinkle, core cells bounce like an equaliser
  - Wi-Fi waves radiate, signal bars sweep, the globe turns, the clock ticks, the hourglass drains and flips, the pulse traces like a heart monitor, and ping beats like a heart
  - A soft glow behind each icon brightens with activity, and turns orange-red past a warning limit
- **Animation settings:** turn animations on or off separately for the bar (**Settings → Appearance → Animation**) and the widget (**Settings → Widget**), and pick a frame rate: 15, 30 or 60 fps.
- **New: tray icon backgrounds.** Choose the tray icon's colour in **Settings → General → Tray icon background**: Violet to cyan (default), Ocean blue, Emerald, Sunset, Crimson, Hot pink, Graphite, Midnight, Light, or no background.
- **Fix: fullscreen hiding is now per monitor.** With **Hide in fullscreen apps** on, only the bar or widget on the same screen as the fullscreen game or video hides. The one on your other monitor stays visible.

---

# PC Stats Bar v1.4.1

- **New: tray icon.** PC Stats Bar now has an icon in the system tray, next to the clock.
  - Click it to open Settings
  - Right-click it for the same menu as the bar, so the menu is still reachable when the bar is hidden (for example by a fullscreen app)
  - Hover over it for a quick CPU / RAM / GPU summary
  - Turn it off in **Settings → General → Show tray icon**
  - Windows 11 puts new tray icons under the **^** arrow at first. Drag it onto the taskbar to keep it visible.

---

# PC Stats Bar v1.4.0

- **New: desktop widget.** A floating panel for your desktop, like LibreHardwareMonitor's gadget but more polished. Turn it on from the bar's right-click menu (**Desktop widget**) or **Settings → Widget**.
  - Stats are grouped under headings (Processor, Graphics, Memory, Network, Storage, System, Battery) with your CPU and GPU model names
  - Each stat has a coloured usage bar and/or a scrolling history graph, and CPU cores get a per-thread bar chart
  - Four styles: Bars, Graphs, Bars and graphs, Minimal
  - Dark, light or match-Windows theme, with custom background colour, opacity, size, width, corner roundness, shadow and an optional title
  - Place it on the desktop behind your windows, as a normal window, or always on top
  - Drag it anywhere. It snaps to screen edges and remembers its position, and can be locked or made click-through.
  - Has its own list of stats and order (drag to rearrange, with dividers), while sharing icons, colours and warning limits with the bar
  - Graph history of 30 seconds to 5 minutes
- Stats are measured only when the bar or the widget shows them, so the widget costs nothing while it's off.

---

# PC Stats Bar v1.3.1

- **Run at startup no longer uses PowerShell.** The startup task is now created with Windows' built-in `schtasks` tool, so antivirus programs (such as Bitdefender) no longer flag it as suspicious. It works the same as before: it starts elevated at sign-in with no UAC prompt, runs on battery, and has no time limit. If you already had Run at startup on, you don't need to change anything.

---

# PC Stats Bar v1.3.0

- **License change:** PC Stats Bar is now free software under the [GNU General Public License v3.0](LICENSE). You can still use, modify and share it freely, but modified versions you distribute must also be GPL with source available. Earlier releases (v1.2.1 and before) remain under the MIT License.
- The About tab shows the new license.

---

# PC Stats Bar v1.2.1

- The license and the exe's company details now name Akila Sella Hennedige as the copyright holder

---

# PC Stats Bar v1.2.0

## What's new
- New **About** tab in Settings showing the version number and copyright (© Akila Sella Hennedige)
- The version shown at the bottom of the Settings sidebar now includes the patch number (e.g. v1.2.0)

Download **`PCStatsBar.exe`** below and replace your old copy (right-click the bar → **Exit** first). Your settings carry over unchanged.

---

# PC Stats Bar v1.1.0

A customisation and efficiency update: pick your own icons and colours per stat, add as many dividers as you like, fine-tune spacing, reorder with a much smoother Arrange page, and choose from 11 new stats. It also uses a fraction of the memory it did before.

Your existing settings carry over. Any of v1.0's three dividers that you had switched on are kept, and the rest are removed.

## Download

Download **`PCStatsBar.exe`** below and replace your old copy. If PC Stats Bar is running, right-click it and choose **Exit** first. It's still a single portable file with no installer.

## What's new

### Icon and colour picker
- Click any stat's icon on the **Arrange** page to open a picker with 37 preset icons
- Choose **No icon** or a short **Text** label (for example `CPU` or `GPU`) instead of an icon
- Give any single stat, or any divider, its own colour from 15 swatches or a custom colour

### Dividers
- Add as many dividers as you want with **+ Add divider**, and remove them with ✕ (or the Delete key)
- Four styles: line, dotted line, dot or blank space, plus an adjustable height

### Spacing and shape
- New **Spacing** sliders: between stats, between icon and value, edge padding, icon size, and line spacing for the two-line layout
- **Corner roundness** slider for the background pill
- **Reset spacing** puts everything back to the defaults

### A better Arrange page
- Rows follow your mouse when you drag them, and the other rows slide out of the way
- The page scrolls automatically when you drag near the top or bottom
- Keyboard support: ↑/↓ to select, **Alt+↑/↓** to move, Space to switch on/off
- **Show switched-off stats** can be turned off to list only what's on your taskbar
- **Reset order** button

### New stats
- **Network:** total speed (upload + download), ping to any address you choose
- **Memory:** committed memory %
- **GPU:** clock speed, fan speed, VRAM usage %
- **Storage:** disk read speed, disk write speed, free space on any drive
- **System:** running processes, uptime
- **Battery:** time left on a laptop battery

### Warning limits
- New **General → Warnings** section to set when values turn to the warning colour: CPU/RAM/GPU usage, CPU/GPU temperature, low battery, high ping and low disk space

### Lower memory use
- Measured side by side with the same settings: memory in use (working set) went from about 100 MB to about 15 MB, the app's own heap from 31 MB to 20 MB, and CPU time was roughly halved
- The bar is only redrawn when something on it has changed, and it's drawn into one reusable buffer instead of a new image every second
- The LibreHardwareMonitor sensor library is only loaded when a stat needs it, and unloads after a minute when nothing does
- Performance counters, the battery scan and ping only run while their stats are shown
- The network adapter list, installed-font list and fonts are cached instead of rebuilt every update
- Memory is handed back to Windows after startup and after closing Settings
- Settings are saved once you stop adjusting, instead of on every slider movement

### Other improvements
- Right-click menu has a new **Arrange stats…** shortcut
- 5-second update interval option
- The version number is shown in the Settings window

## Requirements
- Windows 10 / 11 (64-bit)
- .NET Framework 4.7.2+ (already included in Windows 10 and 11)
- For CPU temperature: admin rights and the PawnIO driver (installed with LibreHardwareMonitor, or from [pawnio.eu](https://pawnio.eu))

## Known limitations
- Batteries of devices on proprietary USB dongles (Razer HyperSpeed, Logitech Lightspeed, SteelSeries, etc.) are only visible to their vendor apps, so they aren't shown. The same devices connected over Bluetooth work.
- Integrated graphics often don't report temperature, power, clock or fan speed.
- Windows SmartScreen may warn you because the exe isn't code-signed. Click **More info → Run anyway**.

## Credits
CPU and AMD/Intel GPU sensors are powered by [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0), which is bundled inside the exe.
