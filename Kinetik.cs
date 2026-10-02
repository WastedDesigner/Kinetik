// Kinetik: a TrafficMonitor-style overlay that sits on the taskbar, left of the tray.
// Shows network speed and ping, CPU (usage, clock, temp, power, per-core), RAM and commit, GPU (usage, temp, clock,
// fan, VRAM, power), disk activity / throughput / free space, processes, uptime, and battery levels of the PC and
// connected Bluetooth devices. Every item can be reordered, given its own icon and colour, and split into groups
// with dividers. CPU temperature/power come from LibreHardwareMonitorLib (in the lib folder) and need admin rights.
// Also a desktop widget, and a game overlay strip with an FPS counter (read from Windows' graphics ETW events).
// Right-click the bar for Settings. Build: build.bat (uses the .NET Framework compiler built into Windows).
//
// Copyright (C) 2026 Akila Sella Hennedige
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the LICENSE
// file or <https://www.gnu.org/licenses/> for details.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Kinetik")]
[assembly: AssemblyDescription("Taskbar overlay showing PC stats and device battery levels")]
[assembly: AssemblyProduct("Kinetik")]
[assembly: AssemblyCompany("Akila Sella Hennedige")]
[assembly: AssemblyCopyright("Copyright © 2026 Akila Sella Hennedige. GNU GPL v3.")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
[assembly: AssemblyInformationalVersion("2.1.0-beta.1")]
// Every native DLL this app imports by name (user32, wlanapi, nvml…) is loaded from System32 only, never from the
// exe's folder or the current directory, so a planted DLL next to Kinetik can't hijack it.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// ======================================================================= Settings
class Settings
{
    public const string Key = @"Software\Kinetik", LegacyKey = @"Software\PCStatsBar";
    public const int CurrentVersion = 2;
    public int SettingsVersion = CurrentVersion;

    // What to show. Each id in StatIds is also the name of its on/off field.
    public bool Up = true, Down = true, NetTotal = false, Ping = false, Wifi = false;
    public bool Cpu = true, CpuCores = false, CpuClock = true, CpuTemp = true, CpuPower = false;
    public bool Ram = true, RamGb = true, RamCommit = false;
    public bool Gpu = true, GpuTemp = true, GpuClock = false, GpuFan = false, GpuVram = false, GpuVramPct = false, GpuPower = false, Fps = false, BaseFps = false;
    public bool FpsLow = false, FpsLow01 = false, FrameTime = false, Latency = false;
    public bool GpuHotspot = false, GpuMemTemp = false, SsdTemp = false, RamTemp = false;
    public bool NetApp = false, PublicIp = false, Vpn = false, Clock = false, Date = false;
    public bool Disk = false, DiskRead = false, DiskWrite = false, DiskFree = false;
    public bool Processes = false, Uptime = false;
    public bool PcBattery = true, BatTime = false, Batteries = true, DeviceNames = false;
    public string Order = DefaultOrder;
    public string GpuSource = ""; // "" = auto, otherwise the GPU's name
    public string PingHost = "1.1.1.1";
    public string FreeDrive = ""; // "" = the drive Windows is on
    public string Custom = "";    // per-item icon and colour: "id=icon|AARRGGBB;..."
    public bool ArrangeShowOff = true;

    // Desktop widget
    public bool WidgetShow = false;
    public string WidgetOrder = DefaultWidgetOrder, WidgetItems = DefaultWidgetItems;
    public int WidgetX = int.MinValue, WidgetY = int.MinValue; // int.MinValue = top-right corner of the main screen
    public int WidgetWidth = 280, WidgetScale = 100, WidgetOpacity = 88, WidgetRound = 100, WidgetGraphSecs = 60;
    public int WidgetStyle = 0; // 0 bars, 1 graphs, 2 bars and graphs, 3 minimal
    public int WidgetTheme = 0; // 0 dark, 1 light, 2 match Windows
    public int WidgetLayer = 0; // 0 on the desktop (behind windows), 1 normal, 2 always on top
    public bool WidgetLocked = false, WidgetClickThrough = false, WidgetSnap = true, WidgetHeadings = true, WidgetHwNames = true, WidgetShadow = true, WidgetAnimate = true, BarAnimate = true;
    public string WidgetTitle = "";
    public Color WidgetBg = Color.FromArgb(0, 0, 0, 0); // fully transparent = the theme's own background colour

    // Game overlay: a slim always-on-top strip with its own stats, for use while gaming
    public bool OverlayShow = false, OverlayLocked = false, OverlayHotkey = true, OverlayText = true;
    public bool OverlayAuto = false;     // show the overlay by itself while a game is in front
    public bool SessionSummary = false;  // log and announce a summary when a game closes
    public bool BenchHotkey = true;      // Ctrl+Shift+F11 records every stat to a CSV file
    public string OverlayOrder = DefaultOverlayOrder, OverlayItems = DefaultOverlayItems;
    public int OverlayX = int.MinValue, OverlayY = int.MinValue; // int.MinValue = top-left corner of the main screen
    public int OverlayScale = 100, OverlayOpacity = 60;

    // Appearance
    public string FontName = DefaultFont();
    public float FontSize = 0; // 0 = fit to taskbar
    public bool TwoLines = false, AutoTheme = true, Pill = true;
    public int PillOpacity = 55;
    public Color ValueColor = Color.White, LabelColor = Color.FromArgb(150, 150, 160), WarnColor = Color.FromArgb(255, 95, 95);
    public Color PillColor = Color.FromArgb(18, 18, 22);
    public Color UpColor = Color.FromArgb(80, 200, 255), DownColor = Color.FromArgb(90, 220, 130);
    public Color CpuColor = Color.FromArgb(255, 200, 60), RamColor = Color.FromArgb(180, 130, 255);
    public Color GpuColor = Color.FromArgb(118, 200, 40), TempColor = Color.FromArgb(255, 140, 80);
    public Color DiskColor = Color.FromArgb(100, 160, 255), BatColor = Color.FromArgb(90, 220, 170);
    public Color MiscColor = Color.FromArgb(240, 150, 200);
    // Spacing, as a percentage of the built-in default (RowGap is in pixels)
    public int GapStats = 100, GapIcon = 100, EdgePad = 100, IconScale = 100, RowGap = 0, PillRound = 100;
    public int DividerStyle = 0, DividerHeight = 100; // style: 0 line, 1 dotted, 2 dot, 3 blank space
    // Warning thresholds
    public int WarnCpu = 90, WarnRam = 90, WarnGpu = 95, WarnCpuTemp = 85, WarnGpuTemp = 83, WarnBattery = 20, WarnPing = 150, WarnFreePct = 10;
    // Behaviour
    public int Interval = 1000, Offset = 0;
    public bool ClickTaskMgr = true, HideFullscreen = true, TrayIcon = true;
    public bool TrayAnimate = true, TaskbarButton = false;
    public bool BarHover = true;      // hovering a stat on the bar shows its last minute and the top process
    public bool AllTaskbars = false;  // also show the bar on other monitors' taskbars
    public bool UpdateBeta = false;   // offer test versions in the update check
    // Notifications
    public bool AlertTemps = true, AlertBattery = true, AlertDisk = true, AlertPing = false;
    public int AnimFps = 60, TrayStyle = 0, AppBackground = 0; // icon animation frame rate; tray icon background (see TrayIconArt.Styles)

    // Every stat that can appear on the bar, in default order. Dividers ("Sep1", "Sep2", ...) are added by the user.
    public const string DefaultOrder = "Up,Down,NetTotal,NetApp,Ping,Wifi,PublicIp,Vpn,Cpu,CpuCores,CpuClock,CpuTemp,CpuPower,Ram,RamGb,RamCommit,RamTemp," +
        "Gpu,GpuTemp,GpuHotspot,GpuMemTemp,GpuClock,GpuFan,GpuVram,GpuVramPct,GpuPower,Fps,BaseFps,FpsLow,FpsLow01,FrameTime,Latency," +
        "Disk,DiskRead,DiskWrite,DiskFree,SsdTemp,Processes,Uptime,Clock,Date,PcBattery,BatTime,Batteries";
    public static readonly string[] StatIds = DefaultOrder.Split(',');

    // The widget has its own order (grouped by hardware) and its own set of stats that are switched on.
    public const string DefaultWidgetOrder = "Cpu,CpuTemp,CpuClock,CpuPower,CpuCores,Gpu,GpuTemp,GpuHotspot,GpuMemTemp,GpuClock,GpuFan,GpuVram,GpuVramPct,GpuPower," +
        "Fps,BaseFps,FpsLow,FpsLow01,FrameTime,Latency,Ram,RamGb,RamCommit,RamTemp,Down,Up,NetTotal,NetApp,Ping,Wifi,PublicIp,Vpn," +
        "Disk,DiskRead,DiskWrite,DiskFree,SsdTemp,Processes,Uptime,Clock,Date,PcBattery,BatTime,Batteries";
    public const string DefaultWidgetItems = "Cpu,CpuTemp,CpuClock,Gpu,GpuTemp,GpuVram,Ram,RamGb,Down,Up,Wifi";
    public static readonly string[] WidgetIds = DefaultWidgetOrder.Split(',');

    // The game overlay likewise has its own order and selection, with the frame rate first.
    public const string DefaultOverlayOrder = "Fps,BaseFps,FpsLow,FpsLow01,FrameTime,Latency,Cpu,CpuTemp,CpuPower,CpuClock,CpuCores," +
        "Gpu,GpuTemp,GpuHotspot,GpuMemTemp,GpuPower,GpuClock,GpuFan,GpuVram,GpuVramPct,Ram,RamGb,RamCommit,RamTemp," +
        "Down,Up,NetTotal,NetApp,Ping,Wifi,PublicIp,Vpn,Disk,DiskRead,DiskWrite,DiskFree,SsdTemp,Processes,Uptime,Clock,Date,PcBattery,BatTime,Batteries";
    public const string DefaultOverlayItems = "Fps,Cpu,CpuTemp,Gpu,GpuTemp,Ram";
    public static readonly string[] OverlayIds = DefaultOverlayOrder.Split(',');

    public static bool IsDivider(string id) { return id.Length > 3 && id.StartsWith("Sep") && id.Substring(3).All(char.IsDigit); }

    public List<string> OrderList() { return Merge(Order, StatIds); }
    public List<string> WidgetOrderList() { return Merge(WidgetOrder, WidgetIds); }
    public List<string> OverlayOrderList() { return Merge(OverlayOrder, OverlayIds); }

    static List<string> Merge(string saved, string[] defaults)
    {
        var list = (saved ?? "").Split(',').Where(id => defaults.Contains(id) || IsDivider(id)).Distinct().ToList();
        // Stats missing from a saved order (e.g. added in a newer version) go next to their default neighbour.
        string prev = null;
        foreach (var id in defaults)
        {
            if (!list.Contains(id)) list.Insert(prev == null ? 0 : list.IndexOf(prev) + 1, id);
            prev = id;
        }
        return list;
    }

    public string NewDividerId()
    {
        var used = OrderList().Concat(WidgetOrderList()).Concat(OverlayOrderList()).ToList(); // unique across both, so per-divider colours don't clash
        int n = 1;
        while (used.Contains("Sep" + n)) n++;
        return "Sep" + n;
    }

    static readonly Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();
    public static FieldInfo Field(string name)
    {
        lock (fieldCache)
        {
            FieldInfo f;
            if (!fieldCache.TryGetValue(name, out f)) fieldCache[name] = f = typeof(Settings).GetField(name);
            return f;
        }
    }

    public bool IsOn(string id) { return IsDivider(id) || (bool)Field(id).GetValue(this); }

    Tuple<string, HashSet<string>> widgetCache, overlayCache; // read from the sampler thread too, so each is swapped as one object
    static bool InItems(string items, ref Tuple<string, HashSet<string>> cache, string id)
    {
        if (IsDivider(id)) return true;
        var src = items ?? "";
        var cc = cache;
        if (cc == null || cc.Item1 != src) cache = cc = Tuple.Create(src, new HashSet<string>(src.Split(',')));
        return cc.Item2.Contains(id);
    }

    static string WithItem(string items, string id, bool on)
    {
        var set = (items ?? "").Split(',').Where(x => x != "" && x != id).ToList();
        if (on) set.Add(id);
        return string.Join(",", set);
    }

    public bool WidgetIsOn(string id) { return InItems(WidgetItems, ref widgetCache, id); }
    public void SetWidgetOn(string id, bool on) { WidgetItems = WithItem(WidgetItems, id, on); }
    public bool OverlayIsOn(string id) { return InItems(OverlayItems, ref overlayCache, id); }
    public void SetOverlayOn(string id, bool on) { OverlayItems = WithItem(OverlayItems, id, on); }

    // True when the bar, the widget or the overlay shows this stat, i.e. when it needs measuring.
    public bool Wants(string id)
    {
        if (IsOn(id) || (WidgetShow && WidgetIsOn(id)) || ((OverlayShow || OverlayAuto) && OverlayIsOn(id))) return true;
        if (AlertTemps && (id == "CpuTemp" || id == "GpuTemp")) return true;
        if (AlertDisk && id == "DiskFree") return true;
        if (AlertPing && id == "Ping") return true;
        if (AlertBattery && id == "Batteries") return true;
        if ((OverlayAuto || SessionSummary || Recording) && (id == "Fps" || id == "FpsLow" || id == "GpuTemp" || id == "CpuTemp")) return true;
        return false;
    }

    // Set while a benchmark CSV is being recorded. A property, so it is never saved with the settings.
    volatile bool recording;
    public bool Recording { get { return recording; } set { recording = value; } }

    // ---- Per-item icon / colour overrides
    string customSrc;
    Dictionary<string, string[]> customMap = new Dictionary<string, string[]>();

    Dictionary<string, string[]> CustomMap()
    {
        if (customSrc != Custom)
        {
            var map = new Dictionary<string, string[]>();
            foreach (var e in (Custom ?? "").Split(';'))
            {
                int eq = e.IndexOf('=');
                if (eq <= 0) continue;
                var v = e.Substring(eq + 1).Split('|');
                map[e.Substring(0, eq)] = new[] { v[0], v.Length > 1 ? v[1] : "" };
            }
            customMap = map; customSrc = Custom;
        }
        return customMap;
    }

    // "" = default icon, "none" = no icon, "text" = short text label, otherwise a preset icon name.
    public string IconOf(string id) { string[] v; return CustomMap().TryGetValue(id, out v) ? v[0] : ""; }

    public Color? ColorOf(string id)
    {
        string[] v; int argb;
        if (CustomMap().TryGetValue(id, out v) && int.TryParse(v[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb)) return Color.FromArgb(argb);
        return null;
    }

    public void SetCustom(string id, string icon, Color? color)
    {
        var map = new Dictionary<string, string[]>(CustomMap());
        if (icon == "" && color == null) map.Remove(id);
        else map[id] = new[] { icon, color == null ? "" : color.Value.ToArgb().ToString("X8") };
        Custom = string.Join(";", map.Select(kv => kv.Key + "=" + kv.Value[0] + "|" + kv.Value[1]));
    }

    static string DefaultFont()
    {
        foreach (var f in new[] { "Cascadia Mono", "Consolas" })
            if (FontList.Has(f)) return f;
        return "Segoe UI";
    }

    // Keeps every number within what the app can handle, so a bad or tampered registry value can't crash it
    // (e.g. a zero timer interval) or make it allocate a huge window.
    public void Sanitize()
    {
        Func<int, int, int, int> cl = (v, lo, hi) => Math.Max(lo, Math.Min(hi, v));
        Interval = cl(Interval, 250, 60000); Offset = cl(Offset, 0, 10000); AnimFps = cl(AnimFps, 10, 120);
        WidgetWidth = cl(WidgetWidth, 120, 2000); WidgetScale = cl(WidgetScale, 30, 400); WidgetOpacity = cl(WidgetOpacity, 0, 100);
        WidgetRound = cl(WidgetRound, 0, 400); WidgetGraphSecs = cl(WidgetGraphSecs, 10, 600);
        WidgetStyle = cl(WidgetStyle, 0, 3); WidgetTheme = cl(WidgetTheme, 0, 2); WidgetLayer = cl(WidgetLayer, 0, 2);
        OverlayScale = cl(OverlayScale, 30, 400); OverlayOpacity = cl(OverlayOpacity, 0, 100);
        PillOpacity = cl(PillOpacity, 0, 100); PillRound = cl(PillRound, 0, 400);
        GapStats = cl(GapStats, 0, 400); GapIcon = cl(GapIcon, 0, 400); EdgePad = cl(EdgePad, 0, 400);
        IconScale = cl(IconScale, 20, 400); RowGap = cl(RowGap, 0, 50);
        DividerStyle = cl(DividerStyle, 0, 3); DividerHeight = cl(DividerHeight, 10, 400);
        TrayStyle = cl(TrayStyle, 0, TrayIconArt.Styles.Length - 1); AppBackground = cl(AppBackground, 0, Theme.Backgrounds.Length - 1);
        if (float.IsNaN(FontSize) || FontSize < 0 || FontSize > 72) FontSize = 0;
        if (string.IsNullOrWhiteSpace(FontName) || FontName.Length > 100) FontName = DefaultFont();
        if (string.IsNullOrWhiteSpace(PingHost) || PingHost.Length > 253) PingHost = "1.1.1.1";
    }

    IEnumerable<FieldInfo> Fields() { return GetType().GetFields(BindingFlags.Public | BindingFlags.Instance); }

    public void Load()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
        {
            if (k == null) return;
            foreach (var f in Fields())
            {
                var v = k.GetValue(f.Name);
                if (v == null) continue;
                try
                {
                    if (f.FieldType == typeof(bool)) f.SetValue(this, Convert.ToInt32(v) == 1);
                    else if (f.FieldType == typeof(int)) f.SetValue(this, Convert.ToInt32(v));
                    else if (f.FieldType == typeof(float)) f.SetValue(this, float.Parse(v.ToString(), CultureInfo.InvariantCulture));
                    else if (f.FieldType == typeof(string)) f.SetValue(this, v.ToString());
                    else if (f.FieldType == typeof(Color)) f.SetValue(this, Color.FromArgb(Convert.ToInt32(v)));
                }
                catch { }
            }
            Sanitize();
            if (Convert.ToInt32(k.GetValue("SettingsVersion", 1)) < 2)
            {
                // v1.0 had three fixed dividers with on/off switches; keep only the ones that were switched on.
                Order = string.Join(",", OrderList().Where(id => !IsDivider(id) || Convert.ToInt32(k.GetValue(id, 0)) == 1));
                SettingsVersion = CurrentVersion;
            }
        }
    }

    public void Save()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
            foreach (var f in Fields())
            {
                var v = f.GetValue(this);
                if (v is bool) k.SetValue(f.Name, (bool)v ? 1 : 0);
                else if (v is int) k.SetValue(f.Name, (int)v);
                else if (v is float) k.SetValue(f.Name, ((float)v).ToString(CultureInfo.InvariantCulture));
                else if (v is string) k.SetValue(f.Name, (string)v);
                else if (v is Color) k.SetValue(f.Name, ((Color)v).ToArgb());
            }
    }

    public void ResetToDefaults()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Key, false);
        var d = new Settings();
        foreach (var f in Fields()) f.SetValue(this, f.GetValue(d));
    }

    public void ResetSpacing()
    {
        GapStats = GapIcon = EdgePad = IconScale = PillRound = DividerHeight = 100;
        RowGap = 0;
    }
}

// Installed font names, enumerated once. (Every FontFamily.Families call allocates a GDI+ object per font.)
static class FontList
{
    static HashSet<string> names;
    public static bool Has(string name)
    {
        if (names == null)
            using (var fc = new InstalledFontCollection())
                names = new HashSet<string>(fc.Families.Select(f => { var n = f.Name; f.Dispose(); return n; }));
        return names.Contains(name);
    }
}

// ======================================================================= Sampling
class Snapshot
{
    static readonly double[] NoCores = new double[0];
    public double Cpu, CpuMhz, Up, Down, Disk = -1, DiskRead = -1, DiskWrite = -1;
    public double[] Cores = NoCores;
    public double CpuTemp = double.NaN, CpuPower = double.NaN;
    public uint RamLoad; public double RamUsed, RamTotal, Commit;
    public bool HasGpu; public string GpuName = "";
    public double GpuUtil, GpuTemp, VramUsed, VramTotal, GpuPower, GpuMhz, GpuFan = -1;
    public bool GpuFanRpm;
    public double FreeGb = -1, FreePct; public string FreeName = "";
    public int Wifi = -1; public string WifiName = ""; // signal quality 0–100, -1 = not on Wi-Fi
    public int Processes = -1, Ping = -1; // Ping: -1 = no reply yet, -2 = timed out
    public double Uptime;
    public string CpuName = "", CpuTempStatus = "";
    public double Fps = double.NaN; // frame rate of the app in the foreground
    public double BaseFps = double.NaN; // frames the game itself rendered, before frame generation (Reflex games)
    public double FpsLow = double.NaN, FpsLow01 = double.NaN; // 1% / 0.1% lows over the last 10 seconds
    public double FrameTime = double.NaN;                       // average frame time over the last second, ms
    public double[] FrameGraph = new double[0];                 // worst frame time per slice of the last 3 s, 0-100
    public double Latency = double.NaN;                         // Reflex simulation start → present end, ms
    public int GamePid; public string GameName = "";            // the game in front, if one is
    public double GpuHotspot = double.NaN, GpuMemTemp = double.NaN, SsdTemp = double.NaN, RamTemp = double.NaN;
    public string NetAppName = ""; public double NetAppRate = -1;
    public string PublicIp = "", Vpn = null;                    // Vpn: null = not checked, "" = none, else its name
    public string FpsApp = "", FpsStatus = "";
}

// Reads sensors through LibreHardwareMonitorLib: CPU temperature/power (needs admin + PawnIO driver)
// and AMD / Intel GPUs (no admin needed). NVIDIA GPUs are read directly through the driver's nvml.dll.
// Kept in its own class so the library is only loaded once it is actually needed.
class HwSensors
{
    LibreHardwareMonitor.Hardware.Computer computer;
    LibreHardwareMonitor.Hardware.IHardware cpu;
    List<LibreHardwareMonitor.Hardware.IHardware> gpus = new List<LibreHardwareMonitor.Hardware.IHardware>();
    List<LibreHardwareMonitor.Hardware.IHardware> nvGpus = new List<LibreHardwareMonitor.Hardware.IHardware>();
    string bestGpu;
    public string Error = "";
    public bool CpuEnabled, DrivesEnabled;
    DateTime drivesAt = DateTime.MinValue;
    double ssdTemp = double.NaN, ramTemp = double.NaN;

    public bool HasCpu { get { return cpu != null; } }
    public List<string> GpuNames { get { return gpus.Select(g => g.Name).ToList(); } }

    public bool Open(bool withCpu, bool withDrives)
    {
        CpuEnabled = withCpu; DrivesEnabled = withDrives;
        try
        {
            computer = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = withCpu, IsGpuEnabled = true, IsStorageEnabled = withDrives, IsMemoryEnabled = withDrives };
            computer.Open();
            nvGpus = computer.Hardware.Where(h => h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia).ToList();
            cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu);
            gpus = computer.Hardware.Where(h => h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuAmd
                                             || h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuIntel).ToList();
            // "Auto" picks the card with the most dedicated memory, i.e. a discrete GPU over integrated graphics.
            double most = -1;
            foreach (var g in gpus)
            {
                g.Update();
                double mem = Value(g, LibreHardwareMonitor.Hardware.SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
                if (double.IsNaN(mem)) mem = 0;
                if (mem > most) { most = mem; bestGpu = g.Name; }
            }
            if (withCpu && cpu == null) Error = "no CPU found";
            return true;
        }
        catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; return false; }
    }

    public void Close()
    {
        try { if (computer != null) computer.Close(); } catch { }
        computer = null; cpu = null; gpus = new List<LibreHardwareMonitor.Hardware.IHardware>(); nvGpus = new List<LibreHardwareMonitor.Hardware.IHardware>();
    }

    // First sensor of the given type whose name matches one of the candidates (in priority order).
    // An empty candidate list matches any sensor of that type.
    static double Value(LibreHardwareMonitor.Hardware.IHardware hw, LibreHardwareMonitor.Hardware.SensorType type, params string[] names)
    {
        var sensors = hw.Sensors.Where(s => s.SensorType == type && s.Value.HasValue).ToList();
        foreach (var n in names)
        {
            var s = sensors.FirstOrDefault(x => x.Name == n);
            if (s != null) return s.Value.Value;
        }
        return names.Length == 0 && sensors.Count > 0 ? sensors[0].Value.Value : double.NaN;
    }

    // Fills the GPU fields of the snapshot from an AMD / Intel card. Empty name = auto.
    public bool ReadGpu(string name, Snapshot s)
    {
        var g = gpus.FirstOrDefault(x => x.Name == (string.IsNullOrEmpty(name) ? bestGpu : name));
        if (g == null) return false;
        g.Update();
        var T = LibreHardwareMonitor.Hardware.SensorType.Temperature;
        var L = LibreHardwareMonitor.Hardware.SensorType.Load;
        var D = LibreHardwareMonitor.Hardware.SensorType.SmallData;
        var P = LibreHardwareMonitor.Hardware.SensorType.Power;
        var C = LibreHardwareMonitor.Hardware.SensorType.Clock;
        var F = LibreHardwareMonitor.Hardware.SensorType.Fan;
        var Ctl = LibreHardwareMonitor.Hardware.SensorType.Control;

        double util = Value(g, L, "GPU Core", "D3D 3D");
        double temp = Value(g, T, "GPU Core", "GPU Hot Spot");
        if (double.IsNaN(temp)) temp = Value(g, T);
        double used = Value(g, D, "GPU Memory Used", "D3D Dedicated Memory Used");
        double total = Value(g, D, "GPU Memory Total", "D3D Dedicated Memory Total");
        double power = Value(g, P, "GPU Package", "GPU Core");
        if (double.IsNaN(power)) power = Value(g, P);
        double mhz = Value(g, C, "GPU Core");
        double fan = Value(g, Ctl, "GPU Fan");
        if (double.IsNaN(fan)) fan = Value(g, Ctl);
        double rpm = double.IsNaN(fan) ? Value(g, F) : double.NaN;

        s.HasGpu = true; s.GpuName = g.Name;
        s.GpuUtil = double.IsNaN(util) ? 0 : util;
        s.GpuTemp = double.IsNaN(temp) ? 0 : temp;
        s.VramUsed = double.IsNaN(used) ? 0 : used / 1024; // MB → GB
        s.VramTotal = double.IsNaN(total) ? 0 : total / 1024;
        s.GpuPower = double.IsNaN(power) ? 0 : power;
        s.GpuMhz = double.IsNaN(mhz) ? 0 : mhz;
        if (!double.IsNaN(fan)) s.GpuFan = fan;
        else if (!double.IsNaN(rpm)) { s.GpuFan = rpm; s.GpuFanRpm = true; }
        return true;
    }

    // GPU hot spot and memory temperatures of the chosen card (any vendor), and the hottest drive and DIMM.
    // Drives and DIMMs are read every 10 seconds: their temperatures move slowly and SMART queries aren't free.
    public void ReadExtra(string gpuName, bool preferNvidia, bool drives, Snapshot s)
    {
        var T = LibreHardwareMonitor.Hardware.SensorType.Temperature;
        var all = gpus.Concat(nvGpus).ToList();
        var g = all.FirstOrDefault(x => x.Name == gpuName)
             ?? (preferNvidia ? nvGpus.FirstOrDefault() : all.FirstOrDefault(x => x.Name == bestGpu));
        if (g != null)
        {
            g.Update();
            s.GpuHotspot = Value(g, T, "GPU Hot Spot");
            s.GpuMemTemp = Value(g, T, "GPU Memory Junction", "GPU Memory");
        }
        if (!drives || computer == null) return;
        if ((DateTime.UtcNow - drivesAt).TotalSeconds >= 10)
        {
            drivesAt = DateTime.UtcNow;
            ssdTemp = ramTemp = double.NaN;
            foreach (var h in computer.Hardware)
            {
                bool storage = h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Storage;
                if (!storage && h.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Memory) continue;
                try { h.Update(); } catch { continue; }
                foreach (var sub in new[] { h }.Concat(h.SubHardware))
                    foreach (var sn in sub.Sensors)
                    {
                        if (sn.SensorType != T || !sn.Value.HasValue || sn.Value.Value <= 0 || sn.Value.Value > 150) continue;
                        if (storage) ssdTemp = double.IsNaN(ssdTemp) ? sn.Value.Value : Math.Max(ssdTemp, sn.Value.Value);
                        else ramTemp = double.IsNaN(ramTemp) ? sn.Value.Value : Math.Max(ramTemp, sn.Value.Value);
                    }
            }
        }
        s.SsdTemp = ssdTemp; s.RamTemp = ramTemp;
    }

    public void ReadCpu(out double temp, out double power)
    {
        temp = double.NaN; power = double.NaN;
        if (cpu == null) return;
        cpu.Update();
        int bestRank = 99;
        foreach (var s in cpu.Sensors)
        {
            if (!s.Value.HasValue) continue;
            var n = s.Name;
            if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature)
            {
                int rank = n.Contains("Tctl") || n.Contains("Tdie") || n == "CPU Package" ? 0
                         : n.Contains("Package") ? 1 : n.Contains("Average") ? 2 : n.StartsWith("Core") ? 3 : 4;
                if (rank < bestRank) { bestRank = rank; temp = s.Value.Value; }
            }
            else if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Power && (n.Contains("Package") || n == "CPU Cores"))
            {
                if (double.IsNaN(power) || n.Contains("Package")) power = s.Value.Value;
            }
        }
    }
}

// Frame rate of the foreground app, counted from the graphics stack's own ETW events (the ones PresentMon uses):
// DXGI and D3D9 "Present" calls for DirectX 9–12 games, and the kernel's present events for OpenGL and Vulkan.
// A real-time trace session needs admin. It only runs while the FPS stat is shown.
// The base frame rate (before DLSS / FSR frame generation adds frames) comes from NVIDIA Reflex's PC Latency Stats
// markers, which Reflex games send for every frame they simulate. While anyone listens to those, Reflex games also
// ping themselves a few times a second to measure latency (as with NVIDIA FrameView), so that provider is only
// switched on while the Base FPS stat is shown.
class FpsMeter
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int StartTraceW(out ulong handle, string name, IntPtr props);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int ControlTraceW(ulong handle, string name, IntPtr props, int code);
    [DllImport("advapi32.dll")] static extern int EnableTraceEx2(ulong handle, ref Guid provider, int control, byte level, ulong any, ulong all, int timeout, IntPtr param);
    [DllImport("advapi32.dll")] static extern ulong OpenTraceW(IntPtr logfile);
    [DllImport("advapi32.dll")] static extern int ProcessTrace(ulong[] handles, int count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] static extern int CloseTrace(ulong handle);
    delegate void RecordCallback(IntPtr record);

    const string SessionName = "Kinetik FPS";
    static readonly Guid Dxgi = new Guid("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
    static readonly Guid D3d9 = new Guid("783aca0a-790e-4d7f-8451-aa850511c6b9");
    static readonly Guid DxgKrnl = new Guid("802ec45a-1e99-4b83-9920-87c98277ba9d");
    static readonly Guid PclStats = new Guid("0d216f06-82a6-4d49-bc4f-8f38ae56efab"); // "PCLStatsTraceLoggingProvider"
    static readonly Guid KernelNet = new Guid("7dd42a49-5329-4832-8dfd-43d979153a88"); // Microsoft-Windows-Kernel-Network
    const int Sources = 8; // 0 DXGI present, 1 D3D9 present, 2-6 kernel blit / flip / present history / present / detailed history,
                           // 7 Reflex simulation start (one per rendered frame)
    const int Presents = 7; // sources below this are displayed frames

    // One process's recent events: present timestamps (QPC ticks) per source for the last 10 seconds (enough for
    // 1% lows), and its Reflex frames in flight and their latencies.
    class Proc
    {
        public readonly Queue<long>[] Times = new Queue<long>[Sources];
        public long Last;
        public readonly Dictionary<ulong, long> SimStart = new Dictionary<ulong, long>(); // Reflex frame id → simulation start
        public readonly Queue<KeyValuePair<long, double>> Latency = new Queue<KeyValuePair<long, double>>(); // (time, ms)
    }

    readonly Dictionary<int, Proc> procs = new Dictionary<int, Proc>();
    Dictionary<int, long[]> net = new Dictionary<int, long[]>(); // pid → { sent, received } bytes since the last TakeNet
    readonly object netLock = new object();
    readonly RecordCallback callback;
    ulong session, trace = ulong.MaxValue;
    bool exitHooked, baseOn, netOn;
    public string Error = "";

    public FpsMeter() { callback = OnEvent; }

    // EVENT_TRACE_PROPERTIES followed by room for the session name.
    static IntPtr Properties()
    {
        const int size = 120 + 1024;
        var p = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
        Marshal.WriteInt32(p, 0, size);     // Wnode.BufferSize
        Marshal.WriteInt32(p, 40, 1);       // Wnode.ClientContext: QPC timestamps
        Marshal.WriteInt32(p, 44, 0x20000); // Wnode.Flags: WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(p, 48, 64);      // BufferSize in KB
        Marshal.WriteInt32(p, 64, 0x100);   // LogFileMode: EVENT_TRACE_REAL_TIME_MODE
        Marshal.WriteInt32(p, 68, 1);       // FlushTimer: deliver events at least every second
        Marshal.WriteInt32(p, 116, 120);    // LoggerNameOffset
        return p;
    }

    static void StopSession()
    {
        var p = Properties();
        try { ControlTraceW(0, SessionName, p, 1); } finally { Marshal.FreeHGlobal(p); } // EVENT_TRACE_CONTROL_STOP
    }

    public bool Start()
    {
        var p = Properties();
        try
        {
            int err = StartTraceW(out session, SessionName, p);
            if (err == 183) { StopSession(); err = StartTraceW(out session, SessionName, p); } // left over from a crash
            if (err != 0) { session = 0; Error = err == 5 ? "needs admin" : "couldn't start (error " + err + ")"; return false; }
        }
        finally { Marshal.FreeHGlobal(p); }
        if (!exitHooked) { exitHooked = true; AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop(); } // the session would otherwise outlive Kinetik

        var dxgi = Dxgi; var d3d9 = D3d9; var krnl = DxgKrnl;
        EnableTraceEx2(session, ref dxgi, 1, 4, 0x2, 0, 0, IntPtr.Zero);       // "Events" keyword: Present start / stop
        EnableTraceEx2(session, ref d3d9, 1, 4, 0x2, 0, 0, IntPtr.Zero);
        EnableTraceEx2(session, ref krnl, 1, 4, 0x8000000, 0, 0, IntPtr.Zero); // "Present" keyword only

        // EVENT_TRACE_LOGFILEW (448 bytes on x64): logger name, process mode, event record callback.
        var log = Marshal.AllocHGlobal(448);
        var name = Marshal.StringToHGlobalUni(SessionName);
        for (int i = 0; i < 448; i++) Marshal.WriteByte(log, i, 0);
        Marshal.WriteIntPtr(log, 8, name);
        Marshal.WriteInt32(log, 28, 0x100 | 0x10000000); // PROCESS_TRACE_MODE_REAL_TIME | EVENT_RECORD
        Marshal.WriteIntPtr(log, 424, Marshal.GetFunctionPointerForDelegate(callback));
        trace = OpenTraceW(log);
        Marshal.FreeHGlobal(log); Marshal.FreeHGlobal(name);
        if (trace == ulong.MaxValue) { Error = "couldn't open the trace"; Stop(); return false; }

        var t = trace;
        new Thread(() => { try { ProcessTrace(new[] { t }, 1, IntPtr.Zero, IntPtr.Zero); } catch { } }) { IsBackground = true, Name = "FPS" }.Start();
        return true;
    }

    public void Stop()
    {
        if (trace != ulong.MaxValue) { CloseTrace(trace); trace = ulong.MaxValue; }
        if (session != 0) { StopSession(); session = 0; }
        baseOn = netOn = false;
    }

    // Switches the Reflex markers on or off for the running session.
    public void SetBase(bool on)
    {
        if (session == 0 || on == baseOn) return;
        var g = PclStats;
        EnableTraceEx2(session, ref g, on ? 1 : 0, 5, 0, 0, 0, IntPtr.Zero); // TraceLogging writes at verbose level, no keywords
        baseOn = on;
    }

    // Switches per-app network accounting (TCP / UDP send and receive sizes) on or off.
    public void SetNet(bool on)
    {
        if (session == 0 || on == netOn) return;
        var g = KernelNet;
        EnableTraceEx2(session, ref g, on ? 1 : 0, 4, 0x30, 0, 0, IntPtr.Zero); // IPv4 | IPv6 keywords
        netOn = on;
        if (!on) lock (netLock) net.Clear();
    }

    // Runs on the trace thread for every event. Reads EVENT_RECORD's header directly.
    void OnEvent(IntPtr rec)
    {
        int provider = Marshal.ReadInt32(rec, 24), id = Marshal.ReadInt16(rec, 40) & 0xFFFF, src; // ProviderId.Data1, EventDescriptor.Id
        int len = Marshal.ReadInt16(rec, 86) & 0xFFFF;
        IntPtr data = Marshal.ReadIntPtr(rec, 96);
        long ts = Marshal.ReadInt64(rec, 16);
        if (provider == 0x7dd42a49)
        {
            // Send / receive events start with the owning process ID and the size. Receives are often logged from
            // another process's context, so the header's process ID can't be used.
            bool send = id == 10 || id == 26 || id == 42 || id == 58, recv = id == 11 || id == 27 || id == 43 || id == 59;
            if ((!send && !recv) || len < 8 || data == IntPtr.Zero) return;
            int npid = Marshal.ReadInt32(data), size = Marshal.ReadInt32(data, 4);
            lock (netLock)
            {
                long[] b;
                if (!net.TryGetValue(npid, out b)) net[npid] = b = new long[2];
                b[send ? 0 : 1] += size;
            }
            return;
        }
        int pid = Marshal.ReadInt32(rec, 12);
        if (provider == 0x0d216f06)
        {
            // PCLStatsEvent (V1-V3) carry UInt32 Marker then UInt64 FrameID; the provider's other events are shorter.
            if (len < 12 || data == IntPtr.Zero) return;
            int marker = Marshal.ReadInt32(data);
            ulong frame = (ulong)Marshal.ReadInt64(data, 4);
            if (marker == 5) // PCLSTATS_PRESENT_END: the frame's latency, from when its simulation started
            {
                lock (procs)
                {
                    Proc lp; long start;
                    if (procs.TryGetValue(pid, out lp) && lp.SimStart.TryGetValue(frame, out start))
                    {
                        lp.SimStart.Remove(frame);
                        lp.Latency.Enqueue(new KeyValuePair<long, double>(ts, (ts - start) * 1000.0 / Stopwatch.Frequency));
                        while (lp.Latency.Count > 0 && lp.Latency.Peek().Key < ts - 2 * Stopwatch.Frequency) lp.Latency.Dequeue();
                    }
                }
                return;
            }
            if (marker != 0) return; // 0 = PCLSTATS_SIMULATION_START
            src = 7;
            lock (procs)
            {
                Proc sp;
                if (!procs.TryGetValue(pid, out sp)) procs[pid] = sp = new Proc();
                if (sp.SimStart.Count > 256) // frames that never presented
                    foreach (var k in sp.SimStart.Where(kv => kv.Value < ts - 2 * Stopwatch.Frequency).Select(kv => kv.Key).ToList()) sp.SimStart.Remove(k);
                sp.SimStart[frame] = ts;
            }
        }
        else if (provider == unchecked((int)0xca11c036)) { if (id != 42) return; src = 0; }
        else if (provider == 0x783aca0a) { if (id != 1) return; src = 1; }
        else if (provider == unchecked((int)0x802ec45a))
        {
            switch (id) { case 166: src = 2; break; case 168: src = 3; break; case 171: src = 4; break; case 184: src = 5; break; case 215: src = 6; break; default: return; }
        }
        else return;
        long keep = ts - (src < Presents ? 10 : 2) * Stopwatch.Frequency;
        lock (procs)
        {
            Proc p;
            if (!procs.TryGetValue(pid, out p)) procs[pid] = p = new Proc();
            var q = p.Times[src] ?? (p.Times[src] = new Queue<long>());
            q.Enqueue(ts);
            while (q.Count > 0 && q.Peek() < keep) q.Dequeue();
            p.Last = Math.Max(p.Last, ts);
        }
    }

    // Frames per second over the last second of a queue.
    static double Rate(Queue<long> q, long f)
    {
        if (q == null || q.Count < 2) return double.NaN;
        long last = q.Last(), from = last - f;
        var recent = q.Where(t => t >= from).ToList();
        return recent.Count < 2 ? 0 : (recent.Count - 1) * (double)f / (last - recent[0]);
    }

    // The source that best counts this process's displayed frames: its DirectX presents if it makes any,
    // otherwise the busiest kernel event type (each fires about once per frame). -1 when it isn't presenting.
    static int Source(Proc p, long f)
    {
        int best = -1; double bestFps = double.NaN;
        for (int src = 0; src < Presents; src++)
        {
            double fps = Rate(p.Times[src], f);
            if (double.IsNaN(fps)) continue;
            if (src <= 1 && fps > 0) return src;
            if (src > 1 && (double.IsNaN(bestFps) || fps > bestFps)) { bestFps = fps; best = src; }
        }
        return best;
    }

    Proc Live(int pid, long now, long f)
    {
        Proc p;
        return procs.TryGetValue(pid, out p) && now - p.Last <= 3 * f ? p : null; // events can arrive up to a second late
    }

    // Frames per second over the last second of the process's presents, or NaN when it isn't presenting.
    public double Read(int pid)
    {
        long now = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
        lock (procs)
        {
            foreach (var dead in procs.Where(kv => now - kv.Value.Last > 15 * f && kv.Value.SimStart.Count == 0).Select(kv => kv.Key).ToList()) procs.Remove(dead);
            var p = Live(pid, now, f);
            if (p == null) return double.NaN;
            int src = Source(p, f);
            return src < 0 ? double.NaN : Rate(p.Times[src], f);
        }
    }

    // Frame pacing of the process: 1% and 0.1% lows over the last 10 seconds (the frame rate that 99% / 99.9% of
    // frames beat), the average frame time over the last second, and the worst frame time in each tenth of a second
    // over the last 3 seconds, scaled 0-100 for a little graph.
    public void ReadPacing(int pid, out double low1, out double low01, out double frameMs, out double[] graph)
    {
        low1 = low01 = frameMs = double.NaN; graph = new double[0];
        long now = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
        lock (procs)
        {
            var p = Live(pid, now, f);
            if (p == null) return;
            int src = Source(p, f);
            if (src < 0) return;
            var t = p.Times[src].ToArray();
            if (t.Length < 3) return;
            var ms = new double[t.Length - 1];
            for (int i = 1; i < t.Length; i++) ms[i - 1] = (t[i] - t[i - 1]) * 1000.0 / f;
            var sorted = (double[])ms.Clone();
            Array.Sort(sorted);
            Func<double, double> pct = q => sorted[Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1))];
            low1 = 1000 / Math.Max(0.01, pct(0.99));
            low01 = 1000 / Math.Max(0.01, pct(0.999));
            long last = t[t.Length - 1];
            var lastSecond = new List<double>();
            const int slices = 30;
            var worst = new double[slices];
            for (int i = 1; i < t.Length; i++)
            {
                long age = last - t[i];
                if (age <= f) lastSecond.Add(ms[i - 1]);
                int slice = slices - 1 - (int)(age * 10 / f);
                if (slice >= 0 && slice < slices) worst[slice] = Math.Max(worst[slice], ms[i - 1]);
            }
            frameMs = lastSecond.Count > 0 ? lastSecond.Average() : ms[ms.Length - 1];
            double top = Math.Max(1000.0 / 30, worst.Max()); // a 30 fps frame fills a third at most, so spikes stand out
            graph = worst.Select(v => Math.Min(100, v * 100 / top)).ToArray();
        }
    }

    // Rendered frames per second from the game's Reflex markers, or NaN for games without Reflex.
    public double ReadBase(int pid)
    {
        long now = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
        lock (procs)
        {
            Proc p;
            if (!procs.TryGetValue(pid, out p) || p.Times[7] == null || p.Times[7].Count == 0 || now - p.Times[7].Last() > 3 * f) return double.NaN;
            return Rate(p.Times[7], f);
        }
    }

    // Average time from a frame's simulation start to its present over the last second (Reflex games only), in ms.
    public double ReadLatency(int pid)
    {
        long now = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
        lock (procs)
        {
            Proc p;
            if (!procs.TryGetValue(pid, out p) || p.Latency.Count == 0) return double.NaN;
            long last = p.Latency.Last().Key;
            if (now - last > 3 * f) return double.NaN;
            var recent = p.Latency.Where(kv => kv.Key >= last - f).Select(kv => kv.Value).ToList();
            return recent.Count == 0 ? double.NaN : recent.Average();
        }
    }

    // Bytes each process sent and received since the last call.
    public Dictionary<int, long[]> TakeNet()
    {
        lock (netLock)
        {
            var taken = net;
            net = new Dictionary<int, long[]>();
            return taken;
        }
    }
}

// A performance counter that only exists while its stat is shown, so hidden stats cost nothing.
class LazyCounter
{
    readonly string[][] sources; // alternatives, each { category, counter, instance }
    PerformanceCounter pc;
    bool failed;

    public LazyCounter(params string[][] sources) { this.sources = sources; }

    public double Read(bool wanted)
    {
        if (!wanted) { Release(); return double.NaN; }
        if (pc == null)
        {
            if (failed) return double.NaN;
            foreach (var s in sources)
                try { pc = new PerformanceCounter(s[0], s[1], s[2], true); pc.NextValue(); return double.NaN; } // rate counters need two samples
                catch { Release(); }
            failed = true;
            return double.NaN;
        }
        try { return pc.NextValue(); } catch { Release(); return double.NaN; }
    }

    public void Release()
    {
        if (pc == null) return;
        try { pc.Dispose(); } catch { }
        pc = null;
    }
}

class Sampler
{
    [StructLayout(LayoutKind.Sequential)] class MEMSTATUS
    {
        public uint Length = (uint)Marshal.SizeOf(typeof(MEMSTATUS)), Load;
        public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    // NVIDIA driver's management library (ships with the driver in System32).
    [StructLayout(LayoutKind.Sequential)] struct NvUtil { public uint Gpu, Mem; }
    [StructLayout(LayoutKind.Sequential)] struct NvMem { public ulong Total, Free, Used; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, int flags);

    // nvml.dll ships in System32 with current NVIDIA drivers, and in the admin-only NVSMI folder with older ones.
    // Loading it by full path first means the imports below bind to that copy and nothing else.
    static bool LoadNvml()
    {
        foreach (var path in new[] {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll") })
            if (File.Exists(path) && LoadLibraryEx(path, IntPtr.Zero, 0x100) != IntPtr.Zero) return true; // LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
        return false;
    }

    [DllImport("nvml.dll")] static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out IntPtr d);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetName(IntPtr d, StringBuilder name, uint len);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr d, int sensor, out uint t);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr d, out NvUtil u);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetMemoryInfo(IntPtr d, out NvMem m);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetPowerUsage(IntPtr d, out uint mw);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetClockInfo(IntPtr d, int type, out uint mhz);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetFanSpeed(IntPtr d, out uint pct);

    public volatile Snapshot Snap = new Snapshot();
    public volatile Dictionary<string, int> Devices = new Dictionary<string, int>();
    readonly Settings cfg;
    readonly Action onSample;

    readonly LazyCounter cpuTotal = new LazyCounter(new[] { "Processor Information", "% Processor Utility", "_Total" }, new[] { "Processor", "% Processor Time", "_Total" });
    readonly LazyCounter cpuPerf = new LazyCounter(new[] { "Processor Information", "% Processor Performance", "_Total" });
    readonly LazyCounter diskIdle = new LazyCounter(new[] { "PhysicalDisk", "% Idle Time", "_Total" });
    readonly LazyCounter diskRead = new LazyCounter(new[] { "PhysicalDisk", "Disk Read Bytes/sec", "_Total" });
    readonly LazyCounter diskWrite = new LazyCounter(new[] { "PhysicalDisk", "Disk Write Bytes/sec", "_Total" });
    readonly LazyCounter procCount = new LazyCounter(new[] { "System", "Processes", "" });
    // Windows' own CPU package power meter (RAPL, in mW). Needs no admin or driver, so it covers PCs where
    // LibreHardwareMonitor can't read the CPU (no PawnIO, not elevated, or a CPU it doesn't know yet).
    readonly LazyCounter cpuPkgPower = new LazyCounter(new[] { "Energy Meter", "Power", "RAPL_Package0_PKG" });
    List<PerformanceCounter> cores, thermalZones;
    double maxMhz;
    string cpuName = "";
    IntPtr gpu = IntPtr.Zero; string gpuName = "";

    NetworkInterface[] nics; string nicKey = ""; DateTime nicsAt;
    long lastRx, lastTx; DateTime lastNet = DateTime.MinValue;

    HwSensors sensors; string sensorStatus = ""; bool sensorsFailed;
    FpsMeter fpsMeter; string fpsError; uint fpsPid; string fpsApp = "";
    DateTime sensorsIdleSince = DateTime.MaxValue;
    volatile bool gpuScanRequested;
    volatile List<string> lhmGpus = new List<string>();

    double freeGb = -1, freePct; string freeName = "", freeFor; DateTime freeAt;
    System.Threading.Timer pingTimer, batTimer, wifiTimer;
    volatile int pingMs = -1, wifiPct = -1;
    volatile string wifiName = "";
    int pinging, readingBatteries;

    public Sampler(Settings cfg, Action onSample) { this.cfg = cfg; this.onSample = onSample; }

    public void Start()
    {
        new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        NetworkChange.NetworkAddressChanged += (s, e) => { nics = null; RefreshIp(); vpnAt = DateTime.MinValue; };
    }

    void Init()
    {
        try
        {
            using (var s = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_Processor"))
            using (var results = s.Get())
                foreach (ManagementObject o in results)
                    using (o) { maxMhz = Convert.ToDouble(o["MaxClockSpeed"]); cpuName = o["Name"].ToString().Trim(); break; }
        }
        catch { }
        try
        {
            if (LoadNvml() && nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out gpu) == 0)
            {
                var sb = new StringBuilder(96);
                nvmlDeviceGetName(gpu, sb, 96); gpuName = sb.ToString();
            }
            else gpu = IntPtr.Zero;
        }
        catch { gpu = IntPtr.Zero; }
    }

    void Loop()
    {
        Init();
        while (true)
        {
            try { Snap = Sample(); onSample(); } catch { }
            Thread.Sleep(Math.Max(250, cfg.Interval));
        }
    }

    static double Clamp(double v) { return Math.Max(0, Math.Min(100, v)); }

    bool AnyGpuStat { get { var c = cfg; return c.Wants("Gpu") || c.Wants("GpuTemp") || c.Wants("GpuClock") || c.Wants("GpuFan") || c.Wants("GpuVram") || c.Wants("GpuVramPct") || c.Wants("GpuPower"); } }
    bool NvidiaChosen { get { return gpu != IntPtr.Zero && (cfg.GpuSource == "" || cfg.GpuSource == gpuName); } }

    Snapshot Sample()
    {
        var c = cfg;
        var now = DateTime.UtcNow;
        var s = new Snapshot { CpuName = cpuName };
        double v;
        v = cpuTotal.Read(c.Wants("Cpu")); if (!double.IsNaN(v)) s.Cpu = Clamp(v);
        v = cpuPerf.Read(c.Wants("CpuClock")); if (!double.IsNaN(v) && maxMhz > 0) s.CpuMhz = v * maxMhz / 100;
        v = diskIdle.Read(c.Wants("Disk")); if (!double.IsNaN(v)) s.Disk = Clamp(100 - v);
        v = diskRead.Read(c.Wants("DiskRead")); if (!double.IsNaN(v)) s.DiskRead = Math.Max(0, v);
        v = diskWrite.Read(c.Wants("DiskWrite")); if (!double.IsNaN(v)) s.DiskWrite = Math.Max(0, v);
        v = procCount.Read(c.Wants("Processes")); if (!double.IsNaN(v)) s.Processes = (int)v;
        s.Cores = ReadCores(c.Wants("CpuCores"));

        var mem = new MEMSTATUS();
        if (GlobalMemoryStatusEx(mem))
        {
            s.RamLoad = mem.Load;
            s.RamTotal = mem.TotalPhys / 1073741824.0;
            s.RamUsed = (mem.TotalPhys - mem.AvailPhys) / 1073741824.0;
            if (mem.TotalPage > 0) s.Commit = (mem.TotalPage - mem.AvailPage) * 100.0 / mem.TotalPage;
        }

        if (c.Wants("Up") || c.Wants("Down") || c.Wants("NetTotal")) ReadNet(s, now);
        else { lastNet = DateTime.MinValue; nics = null; }

        UpdateSensors(now);
        if (AnyGpuStat) ReadGpu(s);
        if (sensors != null && (WantGpuExtra || WantDrives))
            try { sensors.ReadExtra(NvidiaChosen ? gpuName : cfg.GpuSource, NvidiaChosen, WantDrives, s); } catch { }
        ReadTrace(s);
        ManageWeb(s);

        bool wantCpuSensors = c.Wants("CpuTemp") || c.Wants("CpuPower");
        if (!Program.IsAdmin) sensorStatus = "needs admin – right-click → Restart as administrator";
        else if (sensors != null && sensors.HasCpu && wantCpuSensors)
        {
            try
            {
                sensors.ReadCpu(out s.CpuTemp, out s.CpuPower);
                sensorStatus = !double.IsNaN(s.CpuTemp) ? ""
                    : Program.PawnIOInstalled ? "no temperature sensor reported for this CPU"
                    : "the PawnIO driver isn't installed – get it from pawnio.eu";
            }
            catch (Exception e) { sensorStatus = "read failed (" + e.Message + ")"; }
        }
        v = cpuPkgPower.Read(wantCpuSensors && double.IsNaN(s.CpuPower));
        if (!double.IsNaN(v) && v > 0) s.CpuPower = v / 1000; // mW → W
        string status = sensorStatus;
        if (double.IsNaN(s.CpuTemp))
        {
            s.CpuTemp = ReadThermalZone(c.Wants("CpuTemp"));
            if (!double.IsNaN(s.CpuTemp)) status = "approximate, from the ACPI thermal zone" + (sensorStatus != "" ? " (" + sensorStatus + ")" : "");
        }
        else ReadThermalZone(false);
        s.CpuTempStatus = status;

        if (c.Wants("DiskFree")) { ReadFree(now); s.FreeGb = freeGb; s.FreePct = freePct; s.FreeName = freeName; }
        ManagePing();
        s.Ping = c.Wants("Ping") ? pingMs : -1;
        ManageWifi();
        if (c.Wants("Wifi")) { s.Wifi = wifiPct; s.WifiName = wifiName; }
        ManageBatteries();
        s.Uptime = GetTickCount64() / 1000.0;
        return s;
    }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static readonly int OwnPid = Process.GetCurrentProcess().Id;
    // Set by the bar while its hover graph for a network stat is open, so per-app traffic is measured for it.
    public volatile bool NetHover;
    // Latest per-app network rates (bytes/s, sent + received), for the hover graph.
    public volatile List<KeyValuePair<string, double>> NetApps = new List<KeyValuePair<string, double>>();
    DateTime netAt = DateTime.MinValue;
    readonly Dictionary<int, string> procNames = new Dictionary<int, string>();

    string ProcName(int pid)
    {
        string n;
        if (procNames.TryGetValue(pid, out n)) return n;
        if (procNames.Count > 500) procNames.Clear();
        try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName; } catch { n = pid == 0 || pid == 4 ? "System" : "pid " + pid; }
        procNames[pid] = n;
        return n;
    }

    // One trace session covers every stat that comes from Windows' event tracing. It starts when the first of them
    // is needed and stops when none are.
    void ReadTrace(Snapshot s)
    {
        var c = cfg;
        bool pacing = c.Wants("FpsLow") || c.Wants("FpsLow01") || c.Wants("FrameTime") || c.Recording;
        bool wantBase = c.Wants("BaseFps") || c.Wants("Latency");
        bool wantNet = c.Wants("NetApp") || NetHover;
        bool wantFps = c.Wants("Fps") || pacing || wantBase;
        ReadFps(s, wantFps || wantNet, wantBase);
        if (fpsMeter == null) return;
        fpsMeter.SetNet(wantNet);
        int pid = (int)fpsPid;
        if (pacing)
        {
            double[] graph;
            fpsMeter.ReadPacing(pid, out s.FpsLow, out s.FpsLow01, out s.FrameTime, out graph);
            s.FrameGraph = graph;
        }
        if (c.Wants("Latency")) s.Latency = fpsMeter.ReadLatency(pid);

        // A game: the app in front draws its own frames and fills its whole screen (fullscreen or borderless).
        if (!double.IsNaN(s.Fps) && s.Fps >= 15 && pid != OwnPid && fpsApp != "explorer")
        {
            var fg = GetForegroundWindow();
            RECT r;
            if (fg != IntPtr.Zero && GetWindowRect(fg, out r))
            {
                var b = Screen.FromHandle(fg).Bounds;
                if (r.L <= b.Left + 2 && r.T <= b.Top + 2 && r.R >= b.Right - 2 && r.B >= b.Bottom - 2) { s.GamePid = pid; s.GameName = fpsApp; }
            }
        }

        if (wantNet)
        {
            var now = DateTime.UtcNow;
            var bytes = fpsMeter.TakeNet();
            double secs = (now - netAt).TotalSeconds;
            netAt = now;
            if (secs > 0 && secs < 10)
            {
                var rates = bytes.GroupBy(kv => ProcName(kv.Key))
                                 .Select(g => new KeyValuePair<string, double>(g.Key, g.Sum(kv => kv.Value[0] + kv.Value[1]) / secs))
                                 .OrderByDescending(kv => kv.Value).ToList();
                NetApps = rates;
                if (rates.Count > 0) { s.NetAppName = rates[0].Key; s.NetAppRate = rates[0].Value; }
                else { s.NetAppName = ""; s.NetAppRate = 0; }
            }
        }
        else netAt = DateTime.MinValue;
    }

    // ---- Public IP (asked of api.ipify.org every 5 minutes, only while that stat is shown) and VPN status.
    System.Threading.Timer ipTimer;
    volatile string publicIp = "";
    DateTime vpnAt = DateTime.MinValue; string vpnName = null;
    static readonly Regex VpnName = new Regex(@"VPN|WireGuard|TAP-|\bTUN\b|OpenVPN|Tailscale|ZeroTier|NordLynx|Proton|Mullvad|AnyConnect|GlobalProtect|Fortinet|Wintun|Pulse Secure|SonicWall", RegexOptions.IgnoreCase);
    static readonly Regex NotVpn = new Regex(@"Teredo|ISATAP|6to4|IP-HTTPS|Loopback|Hyper-V|vEthernet|VirtualBox|VMware", RegexOptions.IgnoreCase);

    void ManageWeb(Snapshot s)
    {
        if (cfg.Wants("PublicIp"))
        {
            if (ipTimer == null) ipTimer = new System.Threading.Timer(_ => FetchIp(), null, 0, 5 * 60 * 1000);
            s.PublicIp = publicIp;
        }
        else if (ipTimer != null) { ipTimer.Dispose(); ipTimer = null; publicIp = ""; }

        if (cfg.Wants("Vpn"))
        {
            if ((DateTime.UtcNow - vpnAt).TotalSeconds >= 5 || vpnName == null)
            {
                vpnAt = DateTime.UtcNow;
                try
                {
                    var v = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                        && !NotVpn.IsMatch(n.Name + " " + n.Description)
                        && (n.NetworkInterfaceType == NetworkInterfaceType.Ppp || VpnName.IsMatch(n.Name + " " + n.Description)));
                    vpnName = v == null ? "" : v.Name;
                }
                catch { vpnName = ""; }
            }
            s.Vpn = vpnName;
        }
        else vpnName = null;
    }

    public void RefreshIp() { if (ipTimer != null) ipTimer.Change(0, 5 * 60 * 1000); }

    void FetchIp()
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://api.ipify.org");
            req.UserAgent = "Kinetik";
            req.Timeout = req.ReadWriteTimeout = 8000;
            using (var resp = req.GetResponse())
            using (var rd = new StreamReader(resp.GetResponseStream()))
            {
                var text = new string(rd.ReadToEnd().Take(64).ToArray()).Trim();
                System.Net.IPAddress ip;
                publicIp = System.Net.IPAddress.TryParse(text, out ip) ? ip.ToString() : "";
            }
        }
        catch { publicIp = ""; }
    }

    // The trace session starts when the FPS stat is first shown and stops when nothing shows it.
    void ReadFps(Snapshot s, bool wanted, bool wantBase)
    {
        if (!wanted)
        {
            if (fpsMeter != null) { fpsMeter.Stop(); fpsMeter = null; }
            fpsError = null;
            return;
        }
        if (!Program.IsAdmin) { s.FpsStatus = "needs admin – right-click → Restart as administrator"; return; }
        if (fpsMeter == null && fpsError == null)
        {
            var m = new FpsMeter();
            if (m.Start()) fpsMeter = m; else fpsError = m.Error;
        }
        if (fpsMeter == null) { s.FpsStatus = fpsError; return; }
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        if (pid != fpsPid)
        {
            fpsPid = pid; fpsApp = "";
            try { using (var p = Process.GetProcessById((int)pid)) fpsApp = p.ProcessName; } catch { }
        }
        fpsMeter.SetBase(wantBase);
        s.Fps = fpsMeter.Read((int)pid);
        if (cfg.Wants("BaseFps")) s.BaseFps = fpsMeter.ReadBase((int)pid);
        s.FpsApp = fpsApp;
    }

    double[] ReadCores(bool wanted)
    {
        if (!wanted)
        {
            if (cores != null) { foreach (var pc in cores) pc.Dispose(); cores = null; }
            return new double[0];
        }
        if (cores == null)
        {
            cores = new List<PerformanceCounter>();
            try
            {
                var names = new PerformanceCounterCategory("Processor Information").GetInstanceNames()
                    .Where(n => !n.Contains("_Total"))
                    .OrderBy(n => int.Parse(n.Split(',')[0])).ThenBy(n => int.Parse(n.Split(',')[1]));
                foreach (var n in names)
                {
                    var pc = new PerformanceCounter("Processor Information", "% Processor Utility", n, true);
                    pc.NextValue(); cores.Add(pc);
                }
            }
            catch { }
            return new double[0];
        }
        return cores.Select(pc => { try { return Clamp(pc.NextValue()); } catch { return 0.0; } }).ToArray();
    }

    // Fallback CPU temperature: the hottest ACPI thermal zone, which on most laptops sits on or next to the CPU.
    // Desktops often report a fixed dummy value (e.g. 17 °C), so readings outside a believable range are ignored.
    double ReadThermalZone(bool wanted)
    {
        if (!wanted)
        {
            if (thermalZones != null) { foreach (var pc in thermalZones) pc.Dispose(); thermalZones = null; }
            return double.NaN;
        }
        if (thermalZones == null)
        {
            thermalZones = new List<PerformanceCounter>();
            try
            {
                foreach (var n in new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames())
                    thermalZones.Add(new PerformanceCounter("Thermal Zone Information", "Temperature", n, true));
            }
            catch { }
        }
        double best = double.NaN;
        foreach (var pc in thermalZones)
        {
            try
            {
                double t = pc.NextValue() - 273.15; // Kelvin
                if (t >= 25 && t <= 120 && (double.IsNaN(best) || t > best)) best = t;
            }
            catch { }
        }
        return best;
    }

    // The adapter list is cached (enumerating it allocates a lot) and refreshed every 30 s or when the network changes.
    void ReadNet(Snapshot s, DateTime now)
    {
        if (nics == null || (now - nicsAt).TotalSeconds > 30)
        {
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToArray();
            }
            catch { nics = new NetworkInterface[0]; }
            nicsAt = now;
            var key = string.Join(",", nics.Select(n => n.Id));
            if (key != nicKey) { nicKey = key; lastNet = DateTime.MinValue; } // different adapters: restart the baseline
        }
        long rx = 0, tx = 0;
        foreach (var n in nics)
            try { var st = n.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent; } catch { }
        double secs = (now - lastNet).TotalSeconds;
        if (lastNet != DateTime.MinValue && secs > 0)
        {
            s.Down = Math.Max(0, (rx - lastRx) / secs);
            s.Up = Math.Max(0, (tx - lastTx) / secs);
        }
        lastRx = rx; lastTx = tx; lastNet = now;
    }

    // LibreHardwareMonitor is only loaded while something needs it, and closed again after a minute unused.
    void UpdateSensors(DateTime now)
    {
        bool wantCpu = Program.IsAdmin && (cfg.Wants("CpuTemp") || cfg.Wants("CpuPower"));
        bool wantGpu = (AnyGpuStat && !NvidiaChosen) || gpuScanRequested || WantGpuExtra;
        bool wantDrives = WantDrives;
        if (wantCpu || wantGpu || wantDrives)
        {
            sensorsIdleSince = DateTime.MaxValue;
            if (sensors != null && ((wantCpu && !sensors.CpuEnabled) || (wantDrives && !sensors.DrivesEnabled))) CloseSensors();
            if (sensors == null && !sensorsFailed) OpenSensors(wantCpu, wantDrives);
            gpuScanRequested = false;
        }
        else if (sensors != null)
        {
            if (sensorsIdleSince == DateTime.MaxValue) sensorsIdleSince = now;
            else if ((now - sensorsIdleSince).TotalSeconds > 60) { CloseSensors(); Program.TrimMemory(); }
        }
    }

    bool WantGpuExtra { get { return cfg.Wants("GpuHotspot") || cfg.Wants("GpuMemTemp"); } }
    bool WantDrives { get { return Program.IsAdmin && (cfg.Wants("SsdTemp") || cfg.Wants("RamTemp")); } }

    void OpenSensors(bool withCpu, bool withDrives)
    {
        try
        {
            var hw = new HwSensors();
            if (!hw.Open(withCpu, withDrives)) { sensorStatus = "unavailable (" + hw.Error + ")"; sensorsFailed = true; return; }
            sensorStatus = hw.Error != "" ? "unavailable (" + hw.Error + ")" : "";
            sensors = hw;
            lhmGpus = hw.GpuNames;
        }
        catch (IOException) { sensorStatus = "unavailable – keep the lib folder next to Kinetik.exe"; sensorsFailed = true; }
        catch (Exception e) { sensorStatus = "unavailable (" + e.Message + ")"; sensorsFailed = true; }
    }

    void CloseSensors()
    {
        if (sensors != null) sensors.Close();
        sensors = null;
        sensorsIdleSince = DateTime.MaxValue;
    }

    // Asks the sampler to look for AMD / Intel GPUs (for the picker in Settings), even if none are shown.
    public void RequestGpuScan() { if (!sensorsFailed) gpuScanRequested = true; }

    // All GPUs that can be shown: the NVIDIA card (via NVML) first, then AMD / Intel cards.
    public List<string> GpuNames
    {
        get
        {
            var list = new List<string>();
            if (gpu != IntPtr.Zero) list.Add(gpuName);
            list.AddRange(lhmGpus);
            return list;
        }
    }

    // Auto (empty choice) prefers the NVIDIA card, otherwise the AMD / Intel card with the most VRAM.
    void ReadGpu(Snapshot s)
    {
        string want = cfg.GpuSource ?? "";
        if (NvidiaChosen)
        {
            s.HasGpu = true; s.GpuName = gpuName;
            uint t, mw, mhz, fan; NvUtil u; NvMem m;
            if (nvmlDeviceGetTemperature(gpu, 0, out t) == 0) s.GpuTemp = t;
            if (nvmlDeviceGetUtilizationRates(gpu, out u) == 0) s.GpuUtil = u.Gpu;
            if (nvmlDeviceGetMemoryInfo(gpu, out m) == 0) { s.VramUsed = m.Used / 1073741824.0; s.VramTotal = m.Total / 1073741824.0; }
            if (nvmlDeviceGetPowerUsage(gpu, out mw) == 0) s.GpuPower = mw / 1000.0;
            if (nvmlDeviceGetClockInfo(gpu, 0, out mhz) == 0) s.GpuMhz = mhz;
            if (cfg.Wants("GpuFan") && nvmlDeviceGetFanSpeed(gpu, out fan) == 0) s.GpuFan = fan;
            return;
        }
        if (sensors != null)
            try { sensors.ReadGpu(want == gpuName ? "" : want, s); } catch { }
    }

    void ReadFree(DateTime now)
    {
        if (freeFor == cfg.FreeDrive && (now - freeAt).TotalSeconds < 10) return;
        freeFor = cfg.FreeDrive; freeAt = now;
        try
        {
            var d = new DriveInfo(string.IsNullOrEmpty(freeFor) ? Path.GetPathRoot(Environment.SystemDirectory) : freeFor);
            freeGb = d.AvailableFreeSpace / 1073741824.0;
            freePct = d.TotalSize > 0 ? d.AvailableFreeSpace * 100.0 / d.TotalSize : 0;
            freeName = d.Name.TrimEnd('\\');
        }
        catch { freeGb = -1; }
    }

    void ManagePing()
    {
        if (cfg.Wants("Ping") && pingTimer == null) pingTimer = new System.Threading.Timer(_ => DoPing(), null, 0, 2000);
        else if (!cfg.Wants("Ping") && pingTimer != null) { pingTimer.Dispose(); pingTimer = null; pingMs = -1; }
    }

    void ManageWifi()
    {
        if (cfg.Wants("Wifi") && wifiTimer == null) wifiTimer = new System.Threading.Timer(_ => { string n; wifiPct = WifiSignal.Read(out n); wifiName = n; }, null, 0, 2000);
        else if (!cfg.Wants("Wifi") && wifiTimer != null) { wifiTimer.Dispose(); wifiTimer = null; wifiPct = -1; WifiSignal.Close(); }
    }

    void DoPing()
    {
        if (Interlocked.Exchange(ref pinging, 1) == 1) return;
        try
        {
            using (var p = new Ping())
            {
                var r = p.Send(cfg.PingHost, 1000);
                pingMs = r.Status == IPStatus.Success ? (int)r.RoundtripTime : -2;
            }
        }
        catch { pingMs = -2; }
        finally { pinging = 0; }
    }

    void ManageBatteries()
    {
        if (cfg.Wants("Batteries") && batTimer == null) batTimer = new System.Threading.Timer(_ => ReadBatteries(), null, 0, 60000);
        else if (!cfg.Wants("Batteries") && batTimer != null) { batTimer.Dispose(); batTimer = null; Devices = new Dictionary<string, int>(); }
    }

    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // DEVPKEY_Bluetooth_Battery

    public void ReadBatteries()
    {
        if (Interlocked.Exchange(ref readingBatteries, 1) == 1) return;
        var found = new Dictionary<string, int>();
        try
        {
            var q = "SELECT DeviceID, Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTH%' OR PNPDeviceID LIKE 'HID%' OR PNPDeviceID LIKE 'SWD%'";
            using (var searcher = new ManagementObjectSearcher(q))
            using (var results = searcher.Get())
                foreach (ManagementObject dev in results)
                    using (dev)
                    {
                        ManagementBaseObject[] props = null;
                        try
                        {
                            var args = new object[] { new[] { BatteryKey }, null };
                            dev.InvokeMethod("GetDeviceProperties", args);
                            props = args[1] as ManagementBaseObject[];
                            if (props == null || props.Length == 0 || props[0]["Data"] == null) continue;
                            var name = (dev["Name"] ?? "Device").ToString();
                            if (!found.ContainsKey(name)) found[name] = Convert.ToInt32(props[0]["Data"]);
                        }
                        catch { }
                        finally { if (props != null) foreach (var p in props) if (p != null) p.Dispose(); }
                    }
        }
        catch { }
        finally { readingBatteries = 0; }
        if (cfg.Wants("Batteries")) Devices = found;
    }
}

// ======================================================================= Icons
delegate void IconFn(Graphics g, RectangleF r, Color c);

static class Icons
{
    static Pen P(Color c, float s) { return new Pen(c, Math.Max(1.3f, s / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = Math.Max(0.5f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    // Icons the user can pick for any stat, in picker order.
    public static readonly KeyValuePair<string, IconFn>[] Presets =
    {
        K("Upload", Up), K("Download", Down), K("Up / down", UpDown), K("Signal", Signal), K("Wi-Fi", Wifi), K("Globe", Globe),
        K("Chip", Chip), K("Cores", Grid), K("Gauge", Gauge), K("Thermometer", Thermo), K("Flame", Flame), K("Bolt", Bolt),
        K("Plug", Plug), K("Fan", Fan), K("RAM stick", Ram), K("Layers", Layers), K("Stack", Stack), K("Pie", Pie),
        K("Graphics card", Gpu), K("Memory chip", Vram), K("Monitor", Monitor), K("Drive", Disk), K("Drive read", DiskRead), K("Drive write", DiskWrite),
        K("List", List), K("Clock", Clock), K("Hourglass", Hourglass), K("Pulse", Pulse), K("Battery", (g, r, c) => Battery(g, r, c, 70)),
        K("Headphones", Headphones), K("Mouse", Mouse), K("Keyboard", Keyboard), K("Controller", Gamepad), K("Heart", Heart), K("Star", Star),
        K("Dot", Dot), K("Gear", Gear),
    };

    static KeyValuePair<string, IconFn> K(string name, IconFn fn) { return new KeyValuePair<string, IconFn>(name, fn); }

    static Dictionary<string, IconFn> byName;
    public static IconFn Find(string name)
    {
        if (byName == null)
        {
            var d = Presets.ToDictionary(kv => kv.Key, kv => kv.Value);
            d["Divider"] = Divider;
            byName = d;
        }
        IconFn f;
        return byName.TryGetValue(name, out f) ? f : null;
    }

    public static void Up(Graphics g, RectangleF r, Color c) { Arrow(g, r, c, true); }
    public static void Down(Graphics g, RectangleF r, Color c) { Arrow(g, r, c, false); }

    static void Arrow(Graphics g, RectangleF r, Color c, bool up)
    {
        float s = r.Width, cx = r.X + s / 2, t = r.Y + s * 0.15f, b = r.Bottom - s * 0.15f, w = s * 0.28f;
        using (var p = P(c, s * 1.2f))
        {
            g.DrawLine(p, cx, t, cx, b);
            if (up) g.DrawLines(p, new[] { new PointF(cx - w, t + w), new PointF(cx, t), new PointF(cx + w, t + w) });
            else g.DrawLines(p, new[] { new PointF(cx - w, b - w), new PointF(cx, b), new PointF(cx + w, b - w) });
        }
    }

    public static void UpDown(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, w = s * 0.18f, t = r.Y + s * 0.12f, b = r.Bottom - s * 0.12f, x1 = r.X + s * 0.3f, x2 = r.X + s * 0.7f;
        using (var p = P(c, s * 1.1f))
        {
            g.DrawLine(p, x1, t, x1, b); g.DrawLines(p, new[] { new PointF(x1 - w, t + w), new PointF(x1, t), new PointF(x1 + w, t + w) });
            g.DrawLine(p, x2, t, x2, b); g.DrawLines(p, new[] { new PointF(x2 - w, b - w), new PointF(x2, b), new PointF(x2 + w, b - w) });
        }
    }

    public static void Chip(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, i = s * 0.22f;
        var body = new RectangleF(r.X + i, r.Y + i, s - 2 * i, s - 2 * i);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.08f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            g.FillRectangle(b, body.X + body.Width * 0.3f, body.Y + body.Height * 0.3f, body.Width * 0.4f, body.Height * 0.4f);
            for (int k = 1; k <= 3; k++)
            {
                float o = body.X + body.Width * k / 4f, oy = body.Y + body.Height * k / 4f;
                g.DrawLine(p, o, r.Y + s * 0.06f, o, body.Y); g.DrawLine(p, o, body.Bottom, o, r.Bottom - s * 0.06f);
                g.DrawLine(p, r.X + s * 0.06f, oy, body.X, oy); g.DrawLine(p, body.Right, oy, r.Right - s * 0.06f, oy);
            }
        }
    }

    public static void Gauge(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, cy = r.Y + s * 0.6f, rad = s * 0.42f;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            g.DrawArc(p, cx - rad, cy - rad, rad * 2, rad * 2, 160, 220);
            double a = -35 * Math.PI / 180;
            g.DrawLine(p, cx, cy, cx + (float)Math.Cos(a) * rad * 0.75f, cy + (float)Math.Sin(a) * rad * 0.75f);
            float d = s * 0.16f;
            g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
        }
    }

    public static void Bolt(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, x = r.X, y = r.Y;
        using (var b = new SolidBrush(c))
            g.FillPolygon(b, new[] {
                new PointF(x + s * 0.58f, y + s * 0.04f), new PointF(x + s * 0.2f, y + s * 0.56f), new PointF(x + s * 0.47f, y + s * 0.56f),
                new PointF(x + s * 0.4f, y + s * 0.96f), new PointF(x + s * 0.8f, y + s * 0.42f), new PointF(x + s * 0.53f, y + s * 0.42f) });
    }

    public static void Grid(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cell = s * 0.36f, gap = s * 0.12f, o = (s - cell * 2 - gap) / 2;
        using (var b = new SolidBrush(c))
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    using (var p = Round(new RectangleF(r.X + o + i * (cell + gap), r.Y + o + j * (cell + gap), cell, cell), s * 0.06f))
                        g.FillPath(b, p);
    }

    public static void Ram(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.05f, r.Y + s * 0.25f, s * 0.9f, s * 0.42f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.06f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            for (int k = 0; k < 3; k++)
                g.FillRectangle(b, body.X + body.Width * (0.12f + k * 0.29f), body.Y + body.Height * 0.28f, body.Width * 0.18f, body.Height * 0.44f);
            for (int k = 0; k < 5; k++)
            {
                float x = body.X + body.Width * (0.12f + k * 0.19f);
                g.DrawLine(p, x, body.Bottom, x, r.Y + s * 0.82f);
            }
        }
    }

    public static void Layers(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            Func<float, PointF[]> diamond = y => new[] { new PointF(cx, y - s * 0.2f), new PointF(r.X + s * 0.94f, y), new PointF(cx, y + s * 0.2f), new PointF(r.X + s * 0.06f, y) };
            g.DrawLines(p, new[] { new PointF(r.X + s * 0.06f, r.Y + s * 0.62f), new PointF(cx, r.Y + s * 0.82f), new PointF(r.X + s * 0.94f, r.Y + s * 0.62f) });
            g.FillPolygon(b, diamond(r.Y + s * 0.38f));
        }
    }

    public static void Stack(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
            for (int k = 0; k < 3; k++)
                using (var path = Round(new RectangleF(r.X + s * 0.1f, r.Y + s * (0.12f + k * 0.28f), s * 0.8f, s * 0.2f), s * 0.06f))
                {
                    if (k == 0) g.FillPath(b, path);
                    else g.DrawPath(p, path);
                }
    }

    public static void Pie(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            g.DrawEllipse(p, r.X + s * 0.08f, r.Y + s * 0.08f, s * 0.84f, s * 0.84f);
            g.FillPie(b, r.X + s * 0.2f, r.Y + s * 0.2f, s * 0.6f, s * 0.6f, -90, 250);
        }
    }

    public static void Gpu(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.14f, r.Y + s * 0.2f, s * 0.8f, s * 0.5f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.07f))
        {
            g.DrawLine(p, r.X + s * 0.05f, r.Y + s * 0.12f, r.X + s * 0.05f, r.Y + s * 0.88f);
            g.DrawLine(p, r.X + s * 0.05f, r.Y + s * 0.2f, body.X, r.Y + s * 0.2f);
            g.DrawPath(p, path);
            float fr = body.Height * 0.3f, fx = body.X + body.Width * 0.62f, fy = body.Y + body.Height / 2;
            g.DrawEllipse(p, fx - fr, fy - fr, fr * 2, fr * 2);
            g.DrawLine(p, body.X + body.Width * 0.2f, body.Bottom, body.X + body.Width * 0.2f, r.Y + s * 0.82f);
            g.DrawLine(p, body.X + body.Width * 0.2f, r.Y + s * 0.82f, body.X + body.Width * 0.55f, r.Y + s * 0.82f);
        }
    }

    public static void Thermo(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, br = s * 0.17f, by = r.Bottom - s * 0.22f;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        using (var stem = Round(new RectangleF(cx - s * 0.1f, r.Y + s * 0.06f, s * 0.2f, s * 0.62f), s * 0.1f))
        {
            g.DrawPath(p, stem);
            g.FillEllipse(b, cx - br, by - br, br * 2, br * 2);
            g.DrawLine(p, cx, by, cx, r.Y + s * 0.35f);
        }
    }

    public static void Disk(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.08f, r.Y + s * 0.25f, s * 0.84f, s * 0.5f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.1f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            g.DrawLine(p, body.X + body.Width * 0.15f, body.Y + body.Height / 2, body.X + body.Width * 0.5f, body.Y + body.Height / 2);
            float d = s * 0.12f;
            g.FillEllipse(b, body.Right - body.Width * 0.22f - d / 2, body.Y + body.Height / 2 - d / 2, d, d);
        }
    }

    public static void DiskRead(Graphics g, RectangleF r, Color c) { DiskArrow(g, r, c, true); }
    public static void DiskWrite(Graphics g, RectangleF r, Color c) { DiskArrow(g, r, c, false); }

    static void DiskArrow(Graphics g, RectangleF r, Color c, bool read)
    {
        float s = r.Width, cx = r.X + s / 2;
        var body = new RectangleF(r.X + s * 0.08f, r.Y + s * 0.6f, s * 0.84f, s * 0.32f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.08f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            float d = s * 0.1f;
            g.FillEllipse(b, body.Right - body.Width * 0.2f - d / 2, body.Y + body.Height / 2 - d / 2, d, d);
            float t = r.Y + s * 0.04f, bt = r.Y + s * 0.46f, w = s * 0.18f;
            g.DrawLine(p, cx, t, cx, bt);
            // Read = data coming up out of the drive; write = data going down into it.
            if (read) g.DrawLines(p, new[] { new PointF(cx - w, t + w), new PointF(cx, t), new PointF(cx + w, t + w) });
            else g.DrawLines(p, new[] { new PointF(cx - w, bt - w), new PointF(cx, bt), new PointF(cx + w, bt - w) });
        }
    }

    public static void Signal(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, w = s * 0.18f;
        using (var b = new SolidBrush(c))
            for (int k = 0; k < 4; k++)
            {
                float h = s * (0.25f + k * 0.2f);
                g.FillRectangle(b, r.X + s * 0.06f + k * s * 0.24f, r.Bottom - s * 0.1f - h, w, h);
            }
    }

    public static void Wifi(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, cy = r.Bottom - s * 0.16f;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            for (int k = 1; k <= 3; k++)
            {
                float rad = s * 0.23f * k;
                g.DrawArc(p, cx - rad, cy - rad, rad * 2, rad * 2, 225, 90);
            }
            float d = s * 0.16f;
            g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
        }
    }

    public static void Globe(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        using (var p = P(c, s))
        {
            g.DrawEllipse(p, r.X + s * 0.08f, r.Y + s * 0.08f, s * 0.84f, s * 0.84f);
            g.DrawEllipse(p, r.X + s * 0.32f, r.Y + s * 0.08f, s * 0.36f, s * 0.84f);
            g.DrawLine(p, r.X + s * 0.08f, r.Y + s / 2, r.Right - s * 0.08f, r.Y + s / 2);
        }
    }

    public static void Monitor(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2;
        var body = new RectangleF(r.X + s * 0.06f, r.Y + s * 0.12f, s * 0.88f, s * 0.56f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.07f))
        {
            g.DrawPath(p, path);
            g.DrawLine(p, cx, body.Bottom, cx, r.Y + s * 0.86f);
            g.DrawLine(p, cx - s * 0.22f, r.Y + s * 0.86f, cx + s * 0.22f, r.Y + s * 0.86f);
        }
    }

    public static void List(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, d = s * 0.14f;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
            for (int k = 0; k < 3; k++)
            {
                float y = r.Y + s * (0.22f + k * 0.28f);
                g.FillEllipse(b, r.X + s * 0.08f, y - d / 2, d, d);
                g.DrawLine(p, r.X + s * 0.36f, y, r.Right - s * 0.06f, y);
            }
    }

    public static void Clock(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, cy = r.Y + s / 2;
        using (var p = P(c, s))
        {
            g.DrawEllipse(p, r.X + s * 0.08f, r.Y + s * 0.08f, s * 0.84f, s * 0.84f);
            g.DrawLines(p, new[] { new PointF(cx, cy - s * 0.26f), new PointF(cx, cy), new PointF(cx + s * 0.18f, cy + s * 0.12f) });
        }
    }

    public static void Hourglass(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, cy = r.Y + s / 2, t = r.Y + s * 0.1f, bt = r.Bottom - s * 0.1f, l = r.X + s * 0.24f, rr = r.Right - s * 0.24f;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            g.DrawLine(p, l - s * 0.06f, t, rr + s * 0.06f, t);
            g.DrawLine(p, l - s * 0.06f, bt, rr + s * 0.06f, bt);
            g.DrawLines(p, new[] { new PointF(l, t), new PointF(cx - s * 0.05f, cy), new PointF(l, bt) });
            g.DrawLines(p, new[] { new PointF(rr, t), new PointF(cx + s * 0.05f, cy), new PointF(rr, bt) });
            g.FillPolygon(b, new[] { new PointF(cx, cy + s * 0.12f), new PointF(l + s * 0.08f, bt), new PointF(rr - s * 0.08f, bt) });
        }
    }

    public static void Pulse(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cy = r.Y + s / 2;
        using (var p = P(c, s))
            g.DrawLines(p, new[] {
                new PointF(r.X + s * 0.04f, cy), new PointF(r.X + s * 0.28f, cy), new PointF(r.X + s * 0.4f, r.Y + s * 0.18f),
                new PointF(r.X + s * 0.56f, r.Y + s * 0.82f), new PointF(r.X + s * 0.68f, cy), new PointF(r.Right - s * 0.04f, cy) });
    }

    public static void Fan(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, cy = r.Y + s / 2;
        using (var b = new SolidBrush(c))
        {
            for (int k = 0; k < 3; k++)
                using (var path = new GraphicsPath())
                using (var m = new Matrix())
                {
                    path.AddEllipse(cx - s * 0.14f, cy - s * 0.47f, s * 0.28f, s * 0.42f);
                    m.RotateAt(k * 120 + 20, new PointF(cx, cy));
                    path.Transform(m);
                    g.FillPath(b, path);
                }
            float d = s * 0.2f;
            g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
        }
    }

    public static void Heart(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, x = r.X, y = r.Y, cx = x + s / 2;
        using (var path = new GraphicsPath()) using (var b = new SolidBrush(c))
        {
            path.AddBezier(cx, y + s * 0.3f, cx, y + s * 0.08f, x + s * 0.04f, y + s * 0.08f, x + s * 0.04f, y + s * 0.36f);
            path.AddBezier(x + s * 0.04f, y + s * 0.36f, x + s * 0.04f, y + s * 0.6f, cx, y + s * 0.72f, cx, y + s * 0.92f);
            path.AddBezier(cx, y + s * 0.92f, cx, y + s * 0.72f, x + s * 0.96f, y + s * 0.6f, x + s * 0.96f, y + s * 0.36f);
            path.AddBezier(x + s * 0.96f, y + s * 0.36f, x + s * 0.96f, y + s * 0.08f, cx, y + s * 0.08f, cx, y + s * 0.3f);
            g.FillPath(b, path);
        }
    }

    public static void Star(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, cy = r.Y + s * 0.54f;
        var pts = new PointF[10];
        for (int k = 0; k < 10; k++)
        {
            double a = (-90 + k * 36) * Math.PI / 180;
            float rad = k % 2 == 0 ? s * 0.48f : s * 0.2f;
            pts[k] = new PointF(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad);
        }
        using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
    }

    public static void Dot(Graphics g, RectangleF r, Color c)
    {
        float d = r.Width * 0.44f;
        using (var b = new SolidBrush(c)) g.FillEllipse(b, r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
    }

    public static void Battery(Graphics g, RectangleF r, Color c, int pct)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.04f, r.Y + s * 0.28f, s * 0.8f, s * 0.44f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.08f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            g.FillRectangle(b, body.Right + s * 0.03f, body.Y + body.Height * 0.3f, s * 0.08f, body.Height * 0.4f);
            float inset = s * 0.1f;
            g.FillRectangle(b, body.X + inset, body.Y + inset, Math.Max(0, (body.Width - 2 * inset) * pct / 100f), body.Height - 2 * inset);
        }
    }

    public static void Headphones(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            g.DrawArc(p, r.X + s * 0.12f, r.Y + s * 0.1f, s * 0.76f, s * 0.8f, 180, 180);
            using (var l = Round(new RectangleF(r.X + s * 0.06f, r.Y + s * 0.5f, s * 0.22f, s * 0.38f), s * 0.07f)) g.FillPath(b, l);
            using (var rr = Round(new RectangleF(r.Right - s * 0.28f, r.Y + s * 0.5f, s * 0.22f, s * 0.38f), s * 0.07f)) g.FillPath(b, rr);
        }
    }

    public static void Mouse(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.24f, r.Y + s * 0.06f, s * 0.52f, s * 0.88f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.25f))
        {
            g.DrawPath(p, path);
            g.DrawLine(p, body.X + body.Width / 2, body.Y + s * 0.14f, body.X + body.Width / 2, body.Y + s * 0.32f);
        }
    }

    public static void Keyboard(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.04f, r.Y + s * 0.24f, s * 0.92f, s * 0.52f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.08f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            float d = s * 0.09f;
            for (int row = 0; row < 2; row++)
                for (int k = 0; k < 4; k++)
                    g.FillRectangle(b, body.X + body.Width * (0.14f + k * 0.21f), body.Y + body.Height * (0.22f + row * 0.3f), d, d);
            g.DrawLine(p, body.X + body.Width * 0.3f, body.Y + body.Height * 0.8f, body.X + body.Width * 0.7f, body.Y + body.Height * 0.8f);
        }
    }

    public static void Gamepad(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.04f, r.Y + s * 0.26f, s * 0.92f, s * 0.5f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.22f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            float cx = body.X + body.Width * 0.3f, cy = body.Y + body.Height / 2, a = s * 0.11f;
            g.DrawLine(p, cx - a, cy, cx + a, cy); g.DrawLine(p, cx, cy - a, cx, cy + a);
            float d = s * 0.1f;
            g.FillEllipse(b, body.X + body.Width * 0.66f, cy - d, d, d);
            g.FillEllipse(b, body.X + body.Width * 0.76f, cy, d, d);
        }
    }

    public static void Palette(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, d = s * 0.26f;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            g.DrawEllipse(p, r.X + s * 0.08f, r.Y + s * 0.08f, s * 0.84f, s * 0.84f);
            g.FillEllipse(b, r.X + s * 0.25f, r.Y + s * 0.25f, d * 0.7f, d * 0.7f);
            g.FillEllipse(b, r.X + s * 0.55f, r.Y + s * 0.25f, d * 0.7f, d * 0.7f);
            g.FillEllipse(b, r.X + s * 0.3f, r.Y + s * 0.55f, d * 0.7f, d * 0.7f);
        }
    }

    public static void Gear(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, cy = r.Y + s / 2;
        using (var p = P(c, s * 1.1f))
        {
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                g.DrawLine(p, cx + (float)Math.Cos(a) * s * 0.3f, cy + (float)Math.Sin(a) * s * 0.3f, cx + (float)Math.Cos(a) * s * 0.44f, cy + (float)Math.Sin(a) * s * 0.44f);
            }
            g.DrawEllipse(p, cx - s * 0.28f, cy - s * 0.28f, s * 0.56f, s * 0.56f);
            g.DrawEllipse(p, cx - s * 0.1f, cy - s * 0.1f, s * 0.2f, s * 0.2f);
        }
    }

    public static void Info(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2, d = s * 0.13f;
        using (var p = P(c, s)) using (var b = new SolidBrush(c))
        {
            g.DrawEllipse(p, r.X + s * 0.08f, r.Y + s * 0.08f, s * 0.84f, s * 0.84f);
            g.FillEllipse(b, cx - d / 2, r.Y + s * 0.26f, d, d);
            g.DrawLine(p, cx, r.Y + s * 0.46f, cx, r.Y + s * 0.72f);
        }
    }

    public static void Text(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        using (var p = P(c, s * 1.1f))
        {
            g.DrawLine(p, r.X + s * 0.15f, r.Y + s * 0.18f, r.X + s * 0.85f, r.Y + s * 0.18f);
            g.DrawLine(p, r.X + s * 0.5f, r.Y + s * 0.18f, r.X + s * 0.5f, r.Y + s * 0.85f);
        }
    }

    public static void Spacing(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cy = r.Y + s / 2, w = s * 0.14f;
        using (var p = P(c, s))
        {
            g.DrawLine(p, r.X + s * 0.08f, r.Y + s * 0.15f, r.X + s * 0.08f, r.Bottom - s * 0.15f);
            g.DrawLine(p, r.Right - s * 0.08f, r.Y + s * 0.15f, r.Right - s * 0.08f, r.Bottom - s * 0.15f);
            g.DrawLine(p, r.X + s * 0.24f, cy, r.Right - s * 0.24f, cy);
            g.DrawLines(p, new[] { new PointF(r.X + s * 0.24f + w, cy - w), new PointF(r.X + s * 0.24f, cy), new PointF(r.X + s * 0.24f + w, cy + w) });
            g.DrawLines(p, new[] { new PointF(r.Right - s * 0.24f - w, cy - w), new PointF(r.Right - s * 0.24f, cy), new PointF(r.Right - s * 0.24f - w, cy + w) });
        }
    }

    public static void Flame(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, x = r.X, y = r.Y;
        using (var path = new GraphicsPath()) using (var b = new SolidBrush(c)) using (var inner = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            path.AddBezier(x + s * 0.5f, y + s * 0.95f, x + s * 0.1f, y + s * 0.95f, x + s * 0.1f, y + s * 0.5f, x + s * 0.35f, y + s * 0.3f);
            path.AddBezier(x + s * 0.35f, y + s * 0.3f, x + s * 0.4f, y + s * 0.45f, x + s * 0.48f, y + s * 0.45f, x + s * 0.5f, y + s * 0.05f);
            path.AddBezier(x + s * 0.5f, y + s * 0.05f, x + s * 0.8f, y + s * 0.3f, x + s * 0.92f, y + s * 0.55f, x + s * 0.85f, y + s * 0.72f);
            path.AddBezier(x + s * 0.85f, y + s * 0.72f, x + s * 0.8f, y + s * 0.9f, x + s * 0.65f, y + s * 0.95f, x + s * 0.5f, y + s * 0.95f);
            g.FillPath(b, path);
            g.FillEllipse(inner, x + s * 0.38f, y + s * 0.6f, s * 0.24f, s * 0.3f);
        }
    }

    public static void Plug(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width, cx = r.X + s / 2;
        using (var p = P(c, s * 1.1f)) using (var b = new SolidBrush(c))
        {
            g.DrawLine(p, cx - s * 0.16f, r.Y + s * 0.06f, cx - s * 0.16f, r.Y + s * 0.3f);
            g.DrawLine(p, cx + s * 0.16f, r.Y + s * 0.06f, cx + s * 0.16f, r.Y + s * 0.3f);
            using (var body = Round(new RectangleF(cx - s * 0.3f, r.Y + s * 0.3f, s * 0.6f, s * 0.32f), s * 0.08f)) g.FillPath(b, body);
            g.DrawArc(p, cx - s * 0.2f, r.Y + s * 0.42f, s * 0.4f, s * 0.36f, 0, 90);
            g.DrawLine(p, cx, r.Y + s * 0.78f, cx, r.Y + s * 0.95f);
        }
    }

    public static void Vram(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        var body = new RectangleF(r.X + s * 0.12f, r.Y + s * 0.12f, s * 0.76f, s * 0.76f);
        using (var p = P(c, s)) using (var path = Round(body, s * 0.1f)) using (var b = new SolidBrush(c))
        {
            g.DrawPath(p, path);
            float d = s * 0.13f;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    g.FillRectangle(b, body.X + body.Width * (0.17f + i * 0.27f), body.Y + body.Height * (0.17f + j * 0.27f), d, d);
        }
    }

    public static void Divider(Graphics g, RectangleF r, Color c)
    {
        using (var p = P(c, r.Width)) g.DrawLine(p, r.X + r.Width / 2, r.Y + r.Height * 0.08f, r.X + r.Width / 2, r.Bottom - r.Height * 0.08f);
    }

    public static void Grip(Graphics g, RectangleF r, Color c)
    {
        float d = r.Width * 0.16f;
        using (var b = new SolidBrush(c))
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 3; j++)
                    g.FillEllipse(b, r.X + r.Width * (0.3f + i * 0.28f), r.Y + r.Height * (0.18f + j * 0.28f), d, d);
    }

    public static IconFn ForDevice(string name, int pct)
    {
        var n = name.ToLower();
        if (new[] { "head", "bud", "ear", "airpod", "arctis", "kraken", "barracuda", "blackshark", "hammerhead", "wh-", "wf-", "jabra", "bose", "sony", "audio", "hands-free", "headset", "cloud" }.Any(n.Contains))
            return Headphones;
        if (new[] { "mouse", "mx master", "mx anywhere", "viper", "deathadder", "basilisk", "orochi", "naga", "pro click", "g30", "g50", "g70", "g90", "trackpad" }.Any(n.Contains))
            return Mouse;
        if (new[] { "keyboard", "keys", "k380", "k780", "blackwidow", "huntsman" }.Any(n.Contains))
            return Keyboard;
        if (new[] { "controller", "gamepad", "xbox", "dualsense", "dualshock", "joy-con" }.Any(n.Contains))
            return Gamepad;
        return (g, r, c) => Battery(g, r, c, pct);
    }
}

// Name, icon and colour of each bar item, shared by the bar and the settings pages.
static class Stats
{
    public class Info
    {
        public string Title, Short;
        public IconFn Icon, Default; // Icon is null when the user chose "No icon" or "Text label"
        public bool TextLabel;
        public Color Color;
    }

    // id → title, default icon, colour setting, short text label
    static readonly Dictionary<string, string[]> defs = new Dictionary<string, string[]>
    {
        { "Up", new[] { "Upload speed", "Upload", "UpColor", "UP" } },
        { "Down", new[] { "Download speed", "Download", "DownColor", "DN" } },
        { "NetTotal", new[] { "Total network speed", "Up / down", "DownColor", "NET" } },
        { "Ping", new[] { "Ping", "Signal", "UpColor", "PING" } },
        { "Wifi", new[] { "Wi-Fi signal strength", "Wi-Fi", "DownColor", "WIFI" } },
        { "Cpu", new[] { "CPU usage", "Chip", "CpuColor", "CPU" } },
        { "CpuCores", new[] { "CPU per-core bars", "Cores", "CpuColor", "CORE" } },
        { "CpuClock", new[] { "CPU clock speed", "Gauge", "CpuColor", "CLK" } },
        { "CpuTemp", new[] { "CPU temperature", "Thermometer", "TempColor", "TEMP" } },
        { "CpuPower", new[] { "CPU power draw", "Bolt", "CpuColor", "PWR" } },
        { "Ram", new[] { "RAM usage %", "RAM stick", "RamColor", "RAM" } },
        { "RamGb", new[] { "RAM used (GB)", "Layers", "RamColor", "MEM" } },
        { "RamCommit", new[] { "Committed memory %", "Stack", "RamColor", "CMT" } },
        { "Gpu", new[] { "GPU usage", "Graphics card", "GpuColor", "GPU" } },
        { "GpuTemp", new[] { "GPU temperature", "Flame", "GpuColor", "GTMP" } },
        { "GpuClock", new[] { "GPU clock speed", "Gauge", "GpuColor", "GCLK" } },
        { "GpuFan", new[] { "GPU fan speed", "Fan", "GpuColor", "FAN" } },
        { "GpuVram", new[] { "VRAM used (GB)", "Memory chip", "GpuColor", "VRAM" } },
        { "GpuVramPct", new[] { "VRAM usage %", "Memory chip", "GpuColor", "VRAM" } },
        { "GpuPower", new[] { "GPU power draw", "Plug", "GpuColor", "GPWR" } },
        { "Fps", new[] { "Frame rate (FPS)", "Monitor", "GpuColor", "FPS" } },
        { "BaseFps", new[] { "Base frame rate (without frame generation)", "Layers", "GpuColor", "BASE" } },
        { "FpsLow", new[] { "1% low FPS", "Pulse", "GpuColor", "1%" } },
        { "FpsLow01", new[] { "0.1% low FPS", "Pulse", "GpuColor", "0.1%" } },
        { "FrameTime", new[] { "Frame time graph", "Stack", "GpuColor", "FT" } },
        { "Latency", new[] { "Render latency (Reflex)", "Hourglass", "GpuColor", "LAT" } },
        { "GpuHotspot", new[] { "GPU hot spot temperature", "Thermometer", "GpuColor", "HOT" } },
        { "GpuMemTemp", new[] { "GPU memory temperature", "Memory chip", "GpuColor", "GMEM" } },
        { "SsdTemp", new[] { "Drive temperature (hottest)", "Drive", "DiskColor", "SSD" } },
        { "RamTemp", new[] { "RAM temperature", "RAM stick", "RamColor", "RAMT" } },
        { "NetApp", new[] { "Top network app", "Up / down", "DownColor", "APP" } },
        { "PublicIp", new[] { "Public IP address", "Globe", "UpColor", "IP" } },
        { "Vpn", new[] { "VPN status", "Signal", "UpColor", "VPN" } },
        { "Clock", new[] { "Time", "Clock", "MiscColor", "TIME" } },
        { "Date", new[] { "Date", "Clock", "MiscColor", "DATE" } },
        { "Disk", new[] { "Disk activity", "Drive", "DiskColor", "DISK" } },
        { "DiskRead", new[] { "Disk read speed", "Drive read", "DiskColor", "RD" } },
        { "DiskWrite", new[] { "Disk write speed", "Drive write", "DiskColor", "WR" } },
        { "DiskFree", new[] { "Free disk space", "Pie", "DiskColor", "FREE" } },
        { "Processes", new[] { "Running processes", "List", "MiscColor", "PROC" } },
        { "Uptime", new[] { "Uptime", "Clock", "MiscColor", "UPT" } },
        { "PcBattery", new[] { "PC battery", "Battery", "BatColor", "BAT" } },
        { "BatTime", new[] { "Battery time left", "Hourglass", "BatColor", "LEFT" } },
        { "Batteries", new[] { "Bluetooth device batteries", "Headphones", "BatColor", "BT" } },
    };
    static readonly string[] dividerDef = { "Divider", "Divider", "LabelColor", "|" };

    public static Info Get(string id, Settings c)
    {
        string[] d;
        if (!defs.TryGetValue(id, out d)) d = dividerDef;
        var info = new Info { Title = d[0], Short = d[3], Default = Icons.Find(d[1]) };
        var icon = c.IconOf(id);
        info.TextLabel = icon == "text";
        info.Icon = icon == "" ? info.Default : icon == "none" || icon == "text" ? null : (Icons.Find(icon) ?? info.Default);
        var col = c.ColorOf(id);
        info.Color = col.HasValue ? col.Value : (Color)Settings.Field(d[2]).GetValue(c);
        return info;
    }
}

// ======================================================================= Program
static class Program
{
    public const string AppName = "Kinetik", LegacyName = "PCStatsBar";
    public static readonly bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    // LibreHardwareMonitor reads CPU temperature and power through the PawnIO kernel driver.
    public static readonly bool PawnIOInstalled = CheckPawnIO();
    static bool CheckPawnIO()
    {
        try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO")) return k != null; }
        catch { return false; }
    }
    static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>();

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(int flags);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

    [STAThread]
    static void Main(string[] args)
    {
        // No background GC thread: this app's heap is tiny, so concurrent collection only costs memory.
        GCSettings.LatencyMode = GCLatencyMode.Batch;
        try { SetDefaultDllDirectories(0x800); } catch { } // LOAD_LIBRARY_SEARCH_SYSTEM32
        AppDomain.CurrentDomain.AssemblyResolve += ResolveLib;
        Updates.CleanUp();
        bool created;
        MigrateLegacy();
        using (var mutex = new Mutex(true, AppName, out created))
        {
            // When relaunching (e.g. elevated), wait for the previous instance to exit.
            if (!created && !args.Contains("--restart"))
            {
                try { EventWaitHandle.OpenExisting(AppName + ".Show").Set(); } catch { }
                return;
            }
            if (!created && !Wait(mutex)) return;
            SetProcessDPIAware();
            HardenStartup();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Log(e.Exception);
            var bar = new StatsBar();
            int si = Array.IndexOf(args, "--settings");
            if (si >= 0)
            {
                string page = si + 1 < args.Length && !args[si + 1].StartsWith("--") ? args[si + 1] : null;
                bar.Load += (s, e) => bar.BeginInvoke((Action)(() => bar.OpenSettings(page)));
            }
            Application.Run(bar);
        }
    }

    // Skipped when elevated: an admin process appending to a file in the user's temp folder can be redirected
    // (with links) into a protected file by any program running as the user.
    public static void Log(Exception e)
    {
        if (IsAdmin) return;
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "Kinetik.log"), DateTime.Now + " " + e + Environment.NewLine); } catch { }
    }

    static bool Wait(Mutex m)
    {
        try { return m.WaitOne(15000); } catch (AbandonedMutexException) { return true; }
    }

    // Returns memory the app no longer needs to Windows (after startup, closing Settings, or unloading sensors).
    public static void TrimMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        try { SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); } catch { }
    }

    // LibreHardwareMonitor and its dependencies ship in a lib folder next to the exe. The runtime never looks there
    // by itself, so they only load through here, and only if each file matches the copy this build was made with:
    // a DLL swapped into the folder (which may be user-writable) can't run inside Kinetik, even when it's elevated.
    // Update this list whenever the DLLs in lib\ change.
    static readonly Dictionary<string, string> LibHashes = new Dictionary<string, string>
    {
        { "BlackSharp.Core", "CAFB93AFCC8D8A367E21F619673D05C06887D8964867FED1371F02DED1CD3E23" },
        { "DiskInfoToolkit", "1ACBF51B3C10C51C986CF43021680D34A2E38D9A5BA652BCFA9A1B5F7FC09800" },
        { "HidSharp", "79F2BC8DDFF102E6DBEDE84B2C211A3AEA2ED675BF762FA0F02ECB65F61EE812" },
        { "LibreHardwareMonitorLib", "6EBC194316536BA61AF5BE24508AD9FCBB2ECC685E716C12E787C79530F66BF0" },
        { "RAMSPDToolkit-NDD", "B6882354C7C8EC186617E421507743DBFAE09C5C1FC24CEF76A1D0C0C26651DE" },
        { "System.Buffers", "2D78D770C9CB997199154AE8C018B9F1D1EFBC86729F7264DDE6DBAD2A12CAC3" },
        { "System.CodeDom", "FD9DE6770340B32D1A57E21833620F5F440638CE518A38D55712424162B847E5" },
        { "System.Memory", "D5E8E4866F9CFA66F7765660F84B210198893E55335487AFE5EBDA342C0E913D" },
        { "System.Numerics.Vectors", "20C2FA81B8C70D651099D762954F285FD4F942E63B2D7217C145DAB8D4B2F4C9" },
        { "System.Runtime.CompilerServices.Unsafe", "08CBD7278B66F1E68425A82D4B97181A4130D93E3DD91831407ABA7212CCDACF" },
        { "System.Security.AccessControl", "FF14C5F628B9A6798D173AEFBBA0A43D61E66F715108E2576AC0D3DFAB9071D0" },
        { "System.Security.Principal.Windows", "B4D8E15ADC235D0E858E39B5133E5D00A4BAA8C94F4F39E3B5E791B0F9C0C806" },
        { "System.Threading.AccessControl", "5E3A3902F04F840C0FC1F9C2F249F804F6CFDF7901B2E06778BC2A4603AE4694" },
    };

    static string LibDir(string exe) { return Path.Combine(Path.GetDirectoryName(exe), "lib"); }

    static string Sha256(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var f = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
    }

    // True when lib\<name>.dll next to the given exe is exactly the expected file.
    static bool LibOk(string exe, string name)
    {
        string want, path = Path.Combine(LibDir(exe), name + ".dll");
        try { return LibHashes.TryGetValue(name, out want) && File.Exists(path) && Sha256(path) == want; }
        catch { return false; }
    }

    static Assembly ResolveLib(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name;
        lock (loaded)
        {
            Assembly a = null;
            if (loaded.TryGetValue(name, out a)) return a;
            loaded[name] = null; // the fallbacks below can ask for the same name again; this ends that loop
            if (LibHashes.ContainsKey(name))
            {
                // Only ever the verified file: a missing or changed one fails the load (sensors then report why).
                if (LibOk(Application.ExecutablePath, name)) a = Assembly.LoadFrom(Path.Combine(LibDir(Application.ExecutablePath), name + ".dll"));
                if (a != null || !name.StartsWith("System.")) { loaded[name] = a; return a; }
            }
            // Newer framework-package versions requested by the library (e.g. System.Management) → use the built-in one.
            if (a == null) a = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == name);
            if (a == null)
            {
#pragma warning disable 618
                try { a = Assembly.LoadWithPartialName(name); } catch { }
#pragma warning restore 618
            }
            loaded[name] = a;
            return a;
        }
    }

    public static void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restart") { UseShellExecute = true, Verb = "runas" });
            Application.Exit();
        }
        catch { } // user cancelled UAC
    }

    // ---- Startup.
    // Elevated startup (needed for CPU temperature) is a logon task that runs Kinetik as admin with no UAC prompt.
    // The exe that task runs must be somewhere only admins can change; otherwise any program running as the user
    // could replace it, or drop a DLL beside it, and get admin rights at the next sign-in. So elevated startup always
    // runs a copy installed in Program Files. Without admin rights, startup uses the Run key and Kinetik starts
    // unelevated. Tasks are managed through the Task Scheduler API: no schtasks.exe, and no temporary XML file that
    // another program could swap before it's read.
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string SystemExe(string name) { return Path.Combine(Environment.SystemDirectory, name); }

    static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName); } }
    public static string InstalledExe { get { return Path.Combine(InstallDir, AppName + ".exe"); } }

    static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static object Call(object o, string method, params object[] args) { return o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args); }
    static object Prop(object o, string name, params object[] args) { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, args); }

    static object TaskFolder()
    {
        var svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        Call(svc, "Connect");
        return Call(svc, "GetFolder", "\\");
    }

    // The program a task starts, or null when there's no such task.
    static string TaskCommand(string name)
    {
        try
        {
            var task = Call(TaskFolder(), "GetTask", name);
            var action = Prop(Prop(Prop(task, "Definition"), "Actions"), "Item", 1);
            return Prop(action, "Path") as string;
        }
        catch { return null; }
    }

    static void DeleteTask(string name) { try { Call(TaskFolder(), "DeleteTask", name, 0); } catch { } }

    public static bool TaskExists() { return TaskCommand(AppName) != null; }

    // One-time move from the old "PC Stats Bar" name: copies its settings, and swaps its startup entry for Kinetik's.
    // Deleting the old elevated logon task needs admin, so that part waits until Kinetik runs as administrator.
    public static void MigrateLegacy()
    {
        try
        {
            using (var old = Registry.CurrentUser.OpenSubKey(Settings.LegacyKey))
                if (old != null && Registry.CurrentUser.OpenSubKey(Settings.Key) == null)
                    using (var k = Registry.CurrentUser.CreateSubKey(Settings.Key))
                        foreach (var n in old.GetValueNames()) k.SetValue(n, old.GetValue(n), old.GetValueKind(n));
            using (var k = Registry.CurrentUser.CreateSubKey(Settings.Key))
            {
                if (Convert.ToInt32(k.GetValue("LegacyStartupMoved", 0)) == 1) return;
                bool wanted = false;
                using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                    if (run.GetValue(LegacyName) != null) { run.DeleteValue(LegacyName, false); wanted = true; }
                bool oldTask = TaskCommand(LegacyName) != null;
                if (oldTask && IsAdmin) { DeleteTask(LegacyName); wanted = true; oldTask = false; }
                if (wanted) SetStartup(true);
                if (!oldTask) k.SetValue("LegacyStartupMoved", 1);
            }
        }
        catch { }
    }

    // Runs at every start with admin rights: moves an elevated startup task that points at an unprotected exe
    // (from Kinetik 2.0.0 and earlier) onto the Program Files copy, and keeps that copy up to date.
    static void HardenStartup()
    {
        if (!IsAdmin) return;
        try
        {
            var cmd = TaskCommand(AppName);
            if (cmd == null) return;
            if (!SamePath(cmd, InstalledExe)) { SetStartup(true); return; }
            var me = Application.ExecutablePath;
            if (SamePath(me, InstalledExe) || !File.Exists(InstalledExe)) { if (!File.Exists(InstalledExe)) SetStartup(true); return; }
            // Started by hand from elsewhere (as admin, so with the user's consent): refresh the installed copy if it differs.
            var a = new FileInfo(me); var b = new FileInfo(InstalledExe);
            Version mine, theirs;
            bool known = Version.TryParse(FileVersionInfo.GetVersionInfo(me).FileVersion, out mine) & Version.TryParse(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion, out theirs);
            if (known && (mine > theirs || (mine == theirs && a.Length != b.Length))) File.Copy(me, InstalledExe, true);
            else if (!known || mine < theirs) return; // leave a newer installed copy (and its lib folder) alone
            CopyLib();
        }
        catch { }
    }

    // Brings the installed lib folder in line with this build's, from this exe's own (verified) lib folder.
    static void CopyLib()
    {
        var me = Application.ExecutablePath;
        if (SamePath(me, InstalledExe)) return;
        Directory.CreateDirectory(LibDir(InstalledExe));
        foreach (var name in LibHashes.Keys)
            if (!LibOk(InstalledExe, name) && LibOk(me, name))
                File.Copy(Path.Combine(LibDir(me), name + ".dll"), Path.Combine(LibDir(InstalledExe), name + ".dll"), true);
    }

    // Copies this exe and its lib folder into Program Files (created by an admin process, so it inherits the
    // admin-only write access).
    static bool InstallCopy()
    {
        try
        {
            Directory.CreateDirectory(InstallDir);
            var me = Application.ExecutablePath;
            if (!SamePath(me, InstalledExe)) File.Copy(me, InstalledExe, true);
            CopyLib();
            return File.Exists(InstalledExe);
        }
        catch { return false; }
    }

    static void RemoveInstalledCopy()
    {
        if (SamePath(Application.ExecutablePath, InstalledExe)) return; // can't delete ourselves while running
        try
        {
            if (File.Exists(InstalledExe)) File.Delete(InstalledExe);
            foreach (var name in LibHashes.Keys) { var p = Path.Combine(LibDir(InstalledExe), name + ".dll"); if (File.Exists(p)) File.Delete(p); }
            if (Directory.Exists(LibDir(InstalledExe))) Directory.Delete(LibDir(InstalledExe));
            Directory.Delete(InstallDir);
        }
        catch { }
    }

    public static bool IsStartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            if (k != null && k.GetValue(AppName) != null) return true;
        return TaskExists();
    }

    public static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue(AppName, false);
        if (IsAdmin) DeleteTask(AppName);
        if (!on) { if (IsAdmin) RemoveInstalledCopy(); return; }
        if (IsAdmin && InstallCopy() && CreateLogonTask(InstalledExe, AppName, true)) return;
        // No admin rights (or the copy failed): start unelevated from wherever the exe is.
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
    }

    // Registers a logon task. The XML form is used because the plain options can't turn off "only start on AC power"
    // or the 72-hour run limit, which would stop the bar on laptops or after three days.
    public static bool CreateLogonTask(string exe, string name, bool elevated)
    {
        var user = System.Security.SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo><Description>Starts Kinetik when you sign in.</Description></RegistrationInfo>\r\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>\r\n" +
            "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType>" +
            "<RunLevel>" + (elevated ? "HighestAvailable" : "LeastPrivilege") + "</RunLevel></Principal></Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Enabled>true</Enabled>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\"><Exec><Command>" + System.Security.SecurityElement.Escape(exe) + "</Command></Exec></Actions>\r\n" +
            "</Task>\r\n";
        try
        {
            // TASK_CREATE_OR_UPDATE (6), TASK_LOGON_INTERACTIVE_TOKEN (3)
            Call(TaskFolder(), "RegisterTask", name, xml, 6, null, null, 3, null);
            return true;
        }
        catch { return false; }
    }
}

// ======================================================================= Layered drawing surface
// One reusable DIB for a per-pixel-alpha window, instead of allocating a bitmap + HBITMAP every frame.
class DibSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
    {
        public int Size, Width, Height; public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLEND b, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    IntPtr memDC, dib, oldObj;
    Bitmap bmp;
    int w, h;

    // A premultiplied-alpha bitmap (as UpdateLayeredWindow expects) backed by the DIB, reused while the size stays the same.
    public Bitmap Canvas(int width, int height)
    {
        if (dib != IntPtr.Zero && width == w && height == h) return bmp;
        Free();
        memDC = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFOHEADER { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 }; // negative height = top-down rows
        IntPtr bits;
        dib = CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        oldObj = SelectObject(memDC, dib);
        bmp = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        w = width; h = height;
        return bmp;
    }

    public void Push(IntPtr hwnd, int x, int y)
    {
        var size = new SIZE { CX = w, CY = h };
        var src = new POINT();
        var pos = new POINT { X = x, Y = y };
        var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
        UpdateLayeredWindow(hwnd, IntPtr.Zero, ref pos, ref size, memDC, ref src, 0, ref blend, 2);
    }

    void Free()
    {
        if (bmp != null) { bmp.Dispose(); bmp = null; }
        if (memDC != IntPtr.Zero) { SelectObject(memDC, oldObj); DeleteDC(memDC); memDC = IntPtr.Zero; }
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
    }

    public void Dispose() { Free(); }
}

// ======================================================================= Bar
class StatsBar : Form
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    public readonly Settings Cfg = new Settings();
    public readonly Sampler Sampler;
    public readonly GameWatch Watch;
    Snapshot watched;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly ToolTip tip = new ToolTip { InitialDelay = 400, ShowAlways = true };
    string lastTip = "";
    SettingsForm settingsForm;
    public int TaskbarHeight = 48;

    public StatsBar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Cfg.Load();
        Theme.Use(Cfg.AppBackground);

        menu.Renderer = new DarkMenuRenderer();
        menu.Items.Add("Settings…", null, (s, e) => OpenSettings(null));
        menu.Items.Add("Arrange stats…", null, (s, e) => OpenSettings("Arrange"));
        var widgetItem = new ToolStripMenuItem("Desktop widget");
        widgetItem.Click += (s, e) => SetWidget(!Cfg.WidgetShow);
        var unlockItem = new ToolStripMenuItem("Unlock desktop widget");
        unlockItem.Click += (s, e) => { Cfg.WidgetLocked = false; Cfg.Save(); Render(true); NotifySettings("Widget"); };
        var overlayItem = new ToolStripMenuItem("Game overlay");
        overlayItem.Click += (s, e) => ToggleOverlay();
        var benchItem = new ToolStripMenuItem("Record benchmark");
        benchItem.Click += (s, e) => Watch.ToggleRecording();
        var unlockOverlay = new ToolStripMenuItem("Unlock game overlay");
        unlockOverlay.Click += (s, e) => { Cfg.OverlayLocked = false; Cfg.Save(); Render(true); NotifySettings("Overlay"); };
        menu.Items.Add(widgetItem);
        menu.Items.Add(unlockItem);
        menu.Items.Add(overlayItem);
        menu.Items.Add(unlockOverlay);
        menu.Items.Add(benchItem);
        var profilesItem = new ToolStripMenuItem("Profiles");
        menu.Items.Add(profilesItem);
        menu.Opening += (s, e) =>
        {
            widgetItem.Checked = Cfg.WidgetShow; unlockItem.Visible = Cfg.WidgetShow && Cfg.WidgetLocked;
            overlayItem.Checked = Cfg.OverlayShow || Watch.AutoOverlay; overlayItem.ShortcutKeyDisplayString = Cfg.OverlayHotkey ? "Ctrl+Shift+F10" : null;
            benchItem.Checked = Watch.IsRecording; benchItem.Text = Watch.IsRecording ? "Stop recording benchmark" : "Record benchmark";
            benchItem.ShortcutKeyDisplayString = Cfg.BenchHotkey ? "Ctrl+Shift+F11" : null;
            profilesItem.DropDownItems.Clear();
            foreach (var name in Profiles.List())
            {
                var n = name;
                profilesItem.DropDownItems.Add(n, null, (s2, e2) => { if (Profiles.Load(Cfg, n)) { Theme.Use(Cfg.AppBackground); if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close(); Render(true); } });
            }
            if (profilesItem.DropDownItems.Count == 0) profilesItem.DropDownItems.Add(new ToolStripMenuItem("Save one in Settings → General") { Enabled = false });
            unlockOverlay.Visible = Cfg.OverlayShow && Cfg.OverlayLocked;
        };
        menu.Items.Add("Refresh batteries now", null, (s, e) => ThreadPool.QueueUserWorkItem(_ => Sampler.ReadBatteries()));
        if (!Program.IsAdmin) menu.Items.Add("Restart as administrator (CPU temp)", null, (s, e) => Program.RestartAsAdmin());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Close());

        Sampler = new Sampler(Cfg, () => { try { BeginInvoke((Action)Render); } catch { } });
        Watch = new GameWatch(this);
        hoverTimer.Tick += (s, e) => ShowHover();
        HandleCreated += (s, e) =>
        {
            Sampler.Start(); TrimSoon(15000); ListenForShow();
            if (File.Exists(Shortcuts.DesktopPath)) ThreadPool.QueueUserWorkItem(_ => Shortcuts.CreateDesktop(Cfg.TrayStyle)); // keeps its icon current
        };
        var animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animTimer.Tick += (s, e) => { animTimer.Interval = 1000 / Math.Max(10, Cfg.AnimFps); AnimFrame(); };
        animTimer.Start();
    }

    // Launching Kinetik again (e.g. from the desktop shortcut) signals this event, and the running copy opens Settings.
    void ListenForShow()
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.AppName + ".Show");
        var t = new Thread(() =>
        {
            while (ev.WaitOne())
                try { BeginInvoke((Action)(() => OpenSettings(null))); } catch { return; }
        }) { IsBackground = true };
        t.Start();
    }

    // Trims memory once things have settled (a one-shot timer, so it never runs mid-interaction).
    void TrimSoon(int ms)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (s, e) => { t.Dispose(); Program.TrimMemory(); };
        t.Start();
    }

    public void OpenSettings(string page)
    {
        if (settingsForm == null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(this);
            settingsForm.FormClosed += (s, e) => { settingsForm = null; TrimSoon(1500); };
        }
        if (page != null) settingsForm.ShowPage(page);
        settingsForm.Show(); settingsForm.Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close(); // flushes unsaved settings
        if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } // otherwise a dead icon lingers until hovered
        if (taskButton != null) { taskButton.Dispose(); taskButton = null; }
        base.OnFormClosing(e);
    }

    // ---- Tray icon: click for Settings, right-click for the same menu as the bar.
    // Useful when the bar is hidden (fullscreen apps) or there's no room for it on the taskbar.
    NotifyIcon tray;
    int trayStyle = -1, trayFrame;
    double trayPhase; DateTime trayAt = DateTime.UtcNow;

    // Turns the logo's ring in the tray, faster the busier the CPU is.
    // ---- Optional taskbar button, so Kinetik shows as a running app. Clicking it opens Settings.
    TaskbarButtonForm taskButton;

    void SyncTaskbarButton()
    {
        if (Cfg.TaskbarButton && taskButton == null)
        {
            taskButton = new TaskbarButtonForm(this) { Icon = TrayIconArt.Frame(Cfg.TrayStyle, SystemInformation.IconSize.Width, 0) };
            taskButton.Show();
        }
        else if (!Cfg.TaskbarButton && taskButton != null) { taskButton.Dispose(); taskButton = null; }
        else if (taskButton != null && trayStyle != Cfg.TrayStyle) taskButton.Icon = TrayIconArt.Frame(Cfg.TrayStyle, SystemInformation.IconSize.Width, 0);
    }

    void SpinTray()
    {
        var now = DateTime.UtcNow;
        double dt = Math.Min(0.2, (now - trayAt).TotalSeconds); trayAt = now;
        if ((tray == null && taskButton == null) || !Cfg.TrayAnimate) return;
        trayPhase += dt * (0.4 + Sampler.Snap.Cpu / 100 * 4); // thirds of a turn per second
        int f = (int)(trayPhase * TrayIconArt.Frames) % TrayIconArt.Frames;
        if (f == trayFrame) return;
        trayFrame = f;
        if (tray != null) tray.Icon = TrayIconArt.Frame(trayStyle, SystemInformation.SmallIconSize.Width, f);
        if (taskButton != null) taskButton.Icon = TrayIconArt.Frame(trayStyle, SystemInformation.IconSize.Width, f);
    }
    string trayText;

    void SyncTray()
    {
        if (tray != null && trayStyle != Cfg.TrayStyle)
        {
            trayStyle = Cfg.TrayStyle; trayFrame = 0;
            tray.Icon = TrayIconArt.Frame(trayStyle, SystemInformation.SmallIconSize.Width, 0);
        }
        if (Cfg.TrayIcon && tray == null)
        {
            trayStyle = Cfg.TrayStyle;
            tray = new NotifyIcon { Icon = TrayIconArt.Frame(trayStyle, SystemInformation.SmallIconSize.Width, 0), Text = "Kinetik", ContextMenuStrip = menu };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(null); };
            tray.BalloonTipClicked += (s, e) => { if (noteClick != null) noteClick(); };
            tray.Visible = true;
            trayText = null;
        }
        else if (!Cfg.TrayIcon && tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
    }

    // A short live summary for the tray tooltip (Windows allows at most 63 characters).
    void UpdateTrayText(Snapshot s)
    {
        if (tray == null) return;
        var parts = new List<string>();
        if (Cfg.Wants("Cpu")) parts.Add("CPU " + s.Cpu.ToString("0") + "%");
        parts.Add("RAM " + s.RamLoad + "%");
        if (s.HasGpu) parts.Add("GPU " + s.GpuUtil.ToString("0") + "%");
        var text = "Kinetik\n" + string.Join(" · ", parts);
        if (text.Length > 63) text = text.Substring(0, 63);
        if (text != trayText) { trayText = text; tray.Text = text; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        surface.Dispose();
        if (disposing && widget != null) widget.Dispose();
        if (disposing && overlay != null) overlay.Dispose();
        if (disposing) { foreach (var m in mirrors) m.Dispose(); if (hover != null) hover.Dispose(); hoverTimer.Dispose(); }
        if (disposing) { Watch.StopRecording(); if (noteIcon != null) { noteIcon.Visible = false; noteIcon.Dispose(); } noteTimer.Dispose(); }
        if (disposing) { DisposeFonts(); if (measureG != null) { measureG.Dispose(); measureBmp.Dispose(); } brush.Dispose(); }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000; // LAYERED | TOOLWINDOW | TOPMOST | NOACTIVATE
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnMouseUp(MouseEventArgs e) { Clicked(e.Button); base.OnMouseUp(e); }

    public void Clicked(MouseButtons b)
    {
        HideHover();
        if (b == MouseButtons.Right) menu.Show(Cursor.Position);
        else if (b == MouseButtons.Left && Cfg.ClickTaskMgr)
            try { Process.Start(new ProcessStartInfo(Program.SystemExe("taskmgr.exe")) { UseShellExecute = true }); } catch { }
    }

    // ---- Hover graphs: the last minute of whatever stat is under the mouse, with the top process behind it.
    HoverPopup hover;
    string hoverId;
    readonly System.Windows.Forms.Timer hoverTimer = new System.Windows.Forms.Timer { Interval = 350 };
    readonly Dictionary<string, Queue<double>> hoverHist = new Dictionary<string, Queue<double>>();
    const int HoverSeconds = 60;

    Seg SegAt(Point p)
    {
        if (shownL == null) return null;
        foreach (var col in shownL.Cols)
            foreach (var sg in col)
                if (!sg.IsSep && p.X >= sg.X && p.X < sg.X + sg.W + shownL.SegGap / 2 && (col.Count == 1 || (p.Y >= sg.Y && p.Y < sg.Y + shownL.RowH)))
                    return sg;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Cfg.BarHover)
        {
            var sg = SegAt(e.Location);
            string id = sg == null ? null : sg.Id;
            if (id != hoverId)
            {
                hoverId = id;
                if (id == null) HideHover();
                else if (hover != null && hover.Visible) ShowHover();
                else { hoverTimer.Stop(); hoverTimer.Start(); }
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hoverId = null; HideHover(); base.OnMouseLeave(e); }

    void HideHover()
    {
        hoverTimer.Stop();
        if (hover != null && hover.Visible) hover.Hide();
        Sampler.NetHover = false;
        TopProcs.Reset();
    }

    static string TopKind(string id)
    {
        if (id == "Cpu" || id == "CpuCores" || id == "CpuClock" || id == "CpuPower" || id == "CpuTemp") return "cpu";
        if (id == "Ram" || id == "RamGb" || id == "RamCommit") return "ram";
        if (id == "Gpu" || id == "GpuVram" || id == "GpuVramPct" || id == "GpuPower" || id == "GpuTemp" || id == "GpuHotspot") return "gpu";
        if (id == "Disk" || id == "DiskRead" || id == "DiskWrite") return "disk";
        if (id == "Up" || id == "Down" || id == "NetTotal" || id == "NetApp") return "net";
        return null;
    }

    // A stat's value as a number for its graph, or NaN when it has none.
    public static double Num(string id, Snapshot s)
    {
        switch (id)
        {
            case "Up": return s.Up;
            case "Down": return s.Down;
            case "NetTotal": return s.Up + s.Down;
            case "NetApp": return s.NetAppRate >= 0 ? s.NetAppRate : double.NaN;
            case "Ping": return s.Ping >= 0 ? s.Ping : double.NaN;
            case "Wifi": return s.Wifi >= 0 ? s.Wifi : double.NaN;
            case "Cpu": return s.Cpu;
            case "CpuCores": return s.Cores.Length > 0 ? s.Cores.Average() : double.NaN;
            case "CpuClock": return s.CpuMhz > 0 ? s.CpuMhz : double.NaN;
            case "CpuTemp": return s.CpuTemp;
            case "CpuPower": return s.CpuPower;
            case "Ram": return s.RamLoad;
            case "RamGb": return s.RamUsed;
            case "RamCommit": return s.Commit;
            case "RamTemp": return s.RamTemp;
            case "Gpu": return s.HasGpu ? s.GpuUtil : double.NaN;
            case "GpuTemp": return s.HasGpu ? s.GpuTemp : double.NaN;
            case "GpuHotspot": return s.GpuHotspot;
            case "GpuMemTemp": return s.GpuMemTemp;
            case "GpuClock": return s.HasGpu && s.GpuMhz > 0 ? s.GpuMhz : double.NaN;
            case "GpuFan": return s.HasGpu && s.GpuFan >= 0 ? s.GpuFan : double.NaN;
            case "GpuVram": return s.HasGpu ? s.VramUsed : double.NaN;
            case "GpuVramPct": return s.HasGpu && s.VramTotal > 0 ? s.VramUsed * 100 / s.VramTotal : double.NaN;
            case "GpuPower": return s.HasGpu ? s.GpuPower : double.NaN;
            case "Fps": return s.Fps;
            case "BaseFps": return s.BaseFps;
            case "FpsLow": return s.FpsLow;
            case "FpsLow01": return s.FpsLow01;
            case "FrameTime": return s.FrameTime;
            case "Latency": return s.Latency;
            case "Disk": return s.Disk >= 0 ? s.Disk : double.NaN;
            case "DiskRead": return s.DiskRead >= 0 ? s.DiskRead : double.NaN;
            case "DiskWrite": return s.DiskWrite >= 0 ? s.DiskWrite : double.NaN;
            case "DiskFree": return s.FreeGb >= 0 ? s.FreeGb : double.NaN;
            case "SsdTemp": return s.SsdTemp;
            case "Processes": return s.Processes >= 0 ? s.Processes : double.NaN;
            default: return double.NaN;
        }
    }

    // A number formatted the way that stat shows it.
    public static string FormatValue(string id, double v, Snapshot s)
    {
        switch (id)
        {
            case "Up": case "Down": case "NetTotal": case "NetApp": case "DiskRead": case "DiskWrite": return Speed(v);
            case "Ping": case "FrameTime": case "Latency": return v.ToString(id == "FrameTime" ? "0.0" : "0") + " ms";
            case "CpuClock": return (v / 1000).ToString("0.00") + " GHz";
            case "GpuClock": return v.ToString("0") + " MHz";
            case "CpuTemp": case "GpuTemp": case "GpuHotspot": case "GpuMemTemp": case "SsdTemp": case "RamTemp": return v.ToString("0") + "°C";
            case "CpuPower": case "GpuPower": return v.ToString("0") + " W";
            case "RamGb": case "GpuVram": return v.ToString("0.0") + " GB";
            case "DiskFree": return SizeGb(v);
            case "Fps": case "BaseFps": case "FpsLow": case "FpsLow01": return v.ToString("0") + " fps";
            case "Processes": return v.ToString("0");
            case "GpuFan": return v.ToString("0") + (s.GpuFanRpm ? " rpm" : "%");
            default: return v.ToString("0") + "%";
        }
    }

    void RecordHover(Snapshot s)
    {
        int n = Math.Max(10, HoverSeconds * 1000 / Math.Max(250, Cfg.Interval));
        foreach (var id in Cfg.OrderList())
        {
            if (!Cfg.IsOn(id)) { hoverHist.Remove(id); continue; }
            double v = Num(id, s);
            if (double.IsNaN(v)) continue;
            Queue<double> q;
            if (!hoverHist.TryGetValue(id, out q)) hoverHist[id] = q = new Queue<double>();
            q.Enqueue(v);
            while (q.Count > n) q.Dequeue();
        }
    }

    void ShowHover()
    {
        hoverTimer.Stop();
        if (hoverId == null || shownL == null) return;
        var sg = shownL.Cols.SelectMany(col => col).FirstOrDefault(x => x.Id == hoverId && !x.IsSep);
        if (sg == null) { HideHover(); return; }
        var s = Sampler.Snap;
        string id = hoverId, kind = TopKind(id);
        var info = Stats.Get(id, Cfg);
        string title = info.Title;
        if (kind == "cpu" && s.CpuName != "") title += " · " + s.CpuName;
        else if (kind == "gpu" && s.HasGpu) title += " · " + s.GpuName;
        string value = string.Join("  ", sg.Parts.Select(p => p.Text));
        if (value == "" && sg.Bars != null) value = FormatValue(id, Num(id, s), s);

        Queue<double> q;
        var hist = hoverHist.TryGetValue(id, out q) ? q.ToArray() : new double[0];
        string range = hist.Length > 1 ? "Last " + (hist.Length * Math.Max(250, Cfg.Interval) / 1000) + " s:  min " + FormatValue(id, hist.Min(), s)
            + "  ·  avg " + FormatValue(id, hist.Average(), s) + "  ·  max " + FormatValue(id, hist.Max(), s) : "";

        var lines = new List<string>();
        if ((id == "CpuTemp" || id == "CpuPower") && s.CpuTempStatus != "") lines.Add(s.CpuTempStatus);
        if (id == "Fps" || id == "BaseFps" || id.StartsWith("FpsLow") || id == "FrameTime" || id == "Latency")
        {
            if (s.FpsStatus != "") lines.Add(s.FpsStatus);
            else lines.Add("App in front: " + (s.FpsApp != "" ? s.FpsApp : "none"));
            if ((id == "BaseFps" || id == "Latency") && double.IsNaN(Num(id, s))) lines.Add("Needs a game with NVIDIA Reflex");
        }
        if (id == "Ping") lines.Add("To " + Cfg.PingHost);
        if (id == "Wifi" && s.WifiName != "") lines.Add("Network: " + s.WifiName);
        if (id == "DiskFree" && s.FreeName != "") lines.Add(s.FreeName + " · " + s.FreePct.ToString("0") + "% free");
        if (id == "Vpn" && !string.IsNullOrEmpty(s.Vpn)) lines.Add("Connection: " + s.Vpn);
        if (id == "PublicIp") lines.Add("From api.ipify.org, every 5 minutes");
        if (id == "Batteries") foreach (var d in Sampler.Devices) lines.Add("• " + d.Key + ": " + d.Value + "%");

        if (kind == "net")
        {
            Sampler.NetHover = true;
            if (!Program.IsAdmin) lines.Add("Top apps need admin");
            else foreach (var a in Sampler.NetApps.Take(3)) lines.Add("• " + a.Key + "  " + Speed(a.Value));
        }
        else if (kind != null)
        {
            TopProcs.Sample(kind);
            foreach (var a in TopProcs.Result)
                lines.Add("• " + a.Key + "  " + (kind == "ram" ? SizeGb(a.Value / 1073741824.0) : kind == "disk" ? Speed(a.Value) : a.Value.ToString("0") + "%"));
        }

        if (hover == null) hover = new HoverPopup();
        var pt = PointToScreen(new Point((int)(sg.X + sg.W / 2), 0));
        hover.Set(title, value, info.Color, hist, range, lines, pt.X, lastY);
        if (!hover.Visible) hover.Show();
        hover.Invalidate();
    }

    // ---- Layout model ----
    public class Part { public string Text, Template; public Color Color; public bool Bold; public float W; }
    public class Seg
    {
        public IconFn Icon; public string Label; public Color IconColor; public float LabelW;
        public string Id; public double Act; // what the icon's animation follows
        public List<Part> Parts = new List<Part>();
        public double[] Bars; public Color BarColor;
        public bool IsSep; public float Mark; // divider: size of the line / dot
        public float W, X, Y; // width, and where it was last drawn
    }
    public class BarLayout
    {
        public List<List<Seg>> Cols = new List<List<Seg>>();
        public int Width;
        public float FontPx, Icon, IconGap, PartGap, SegGap, Pad, BarW, BarGap, LineH, RowH;
        public string Key;
    }

    public static string Speed(double b)
    {
        if (b >= 1073741824) return (b / 1073741824).ToString("0.00") + " GB/s";
        if (b >= 1048576) return (b / 1048576).ToString(b >= 104857600 ? "0" : b >= 10485760 ? "0.0" : "0.00") + " MB/s";
        if (b >= 1024) return (b / 1024).ToString("0") + " KB/s";
        return b.ToString("0") + " B/s";
    }

    public static string SizeGb(double gb)
    {
        if (gb >= 1000) return (gb / 1024).ToString("0.0") + " TB";
        return gb.ToString(gb >= 100 ? "0" : "0.0") + " GB";
    }

    public static string Duration(double secs)
    {
        var t = TimeSpan.FromSeconds(secs);
        if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
        if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes.ToString("00") + "m";
        return t.Minutes + "m";
    }

    // The stats in ids that isOn accepts, as segments. Shared with the game overlay, which can force text labels.
    public List<Seg> BuildSegments(Snapshot s, Color val, Color dim, List<string> ids, Func<string, bool> isOn, bool textLabels)
    {
        var c = Cfg;
        var segs = new List<Seg>();
        Func<double, int, Color> warn = (v, limit) => v >= limit ? c.WarnColor : val;

        foreach (var id in ids)
        {
            if (!isOn(id)) continue;
            var info = Stats.Get(id, c);
            bool customIcon = c.IconOf(id) != "", asText = textLabels || info.TextLabel;
            double act = IconAnim.Activity(id, s);
            Func<Seg> seg = () => new Seg { Icon = info.Icon, Label = asText ? info.Short : null, IconColor = info.Color, Id = id, Act = act };
            Action<string, string, Color> add = (text, template, color) =>
            {
                var sg = seg();
                sg.Parts.Add(new Part { Text = text, Template = template, Color = color, Bold = color != dim });
                segs.Add(sg);
            };
            Action<double, int> addTemp = (t, limit) => add(double.IsNaN(t) ? "--°C" : t.ToString("0") + "°C", "100°C", double.IsNaN(t) ? dim : warn(t, limit));
            switch (id)
            {
                case "Up": add(Speed(s.Up), "88.8 MB/s", val); break;
                case "Down": add(Speed(s.Down), "88.8 MB/s", val); break;
                case "NetTotal": add(Speed(s.Up + s.Down), "88.8 MB/s", val); break;
                case "Ping":
                    add(s.Ping >= 0 ? s.Ping + " ms" : "-- ms", "888 ms", s.Ping == -2 || s.Ping >= c.WarnPing ? c.WarnColor : s.Ping < 0 ? dim : val);
                    break;
                case "Wifi": if (s.Wifi >= 0) add(s.Wifi + "%", "100%", s.Wifi <= 25 ? c.WarnColor : val); break;
                case "Cpu": add(s.Cpu.ToString("0") + "%", "100%", warn(s.Cpu, c.WarnCpu)); break;
                case "CpuCores":
                    if (s.Cores.Length > 0) { var sg = seg(); sg.Bars = s.Cores; sg.BarColor = info.Color; segs.Add(sg); }
                    break;
                case "CpuClock": if (s.CpuMhz > 0) add((s.CpuMhz / 1000).ToString("0.00") + " GHz", "8.88 GHz", val); break;
                case "CpuTemp": add(double.IsNaN(s.CpuTemp) ? "--°C" : s.CpuTemp.ToString("0") + "°C", "100°C", double.IsNaN(s.CpuTemp) ? dim : warn(s.CpuTemp, c.WarnCpuTemp)); break;
                case "CpuPower": if (!double.IsNaN(s.CpuPower)) add(s.CpuPower.ToString("0") + " W", "888 W", val); break;
                case "Ram": add(s.RamLoad + "%", "100%", warn(s.RamLoad, c.WarnRam)); break;
                case "RamGb": add(s.RamUsed.ToString("0.0") + "/" + s.RamTotal.ToString("0") + " GB", "88.8/88 GB", val); break;
                case "RamCommit": add(s.Commit.ToString("0") + "%", "100%", warn(s.Commit, c.WarnRam)); break;
                case "Gpu": if (s.HasGpu) add(s.GpuUtil.ToString("0") + "%", "100%", warn(s.GpuUtil, c.WarnGpu)); break;
                case "GpuTemp": if (s.HasGpu) add(s.GpuTemp.ToString("0") + "°C", "100°C", warn(s.GpuTemp, c.WarnGpuTemp)); break;
                case "GpuClock": if (s.HasGpu && s.GpuMhz > 0) add(s.GpuMhz.ToString("0") + " MHz", "8888 MHz", val); break;
                case "GpuFan":
                    if (s.HasGpu && s.GpuFan >= 0)
                        add(s.GpuFan.ToString("0") + (s.GpuFanRpm ? " rpm" : "%"), s.GpuFanRpm ? "8888 rpm" : "100%", val);
                    break;
                case "GpuVram": if (s.HasGpu) add(s.VramUsed.ToString("0.0") + "/" + s.VramTotal.ToString("0") + " GB", "88.8/88 GB", val); break;
                case "GpuVramPct":
                    if (s.HasGpu && s.VramTotal > 0) { double p = s.VramUsed * 100 / s.VramTotal; add(p.ToString("0") + "%", "100%", warn(p, c.WarnRam)); }
                    break;
                case "GpuPower": if (s.HasGpu) add(s.GpuPower.ToString("0") + " W", "888 W", val); break;
                case "Fps":
                {
                    string v = double.IsNaN(s.Fps) ? "--" : s.Fps.ToString("0");
                    add(asText ? v : v + " fps", asText ? "888" : "888 fps", double.IsNaN(s.Fps) ? dim : val); // "FPS 144", not "FPS 144 fps"
                    break;
                }
                case "BaseFps":
                {
                    string v = double.IsNaN(s.BaseFps) ? "--" : s.BaseFps.ToString("0");
                    add(asText ? v : v + " base", asText ? "888" : "888 base", double.IsNaN(s.BaseFps) ? dim : val);
                    break;
                }
                case "FpsLow": case "FpsLow01":
                {
                    double low = id == "FpsLow" ? s.FpsLow : s.FpsLow01;
                    string v = double.IsNaN(low) ? "--" : low.ToString("0");
                    add(asText ? v : v + (id == "FpsLow" ? " 1%" : " .1%"), asText ? "888" : "888 .1%", double.IsNaN(low) ? dim : val);
                    break;
                }
                case "FrameTime":
                {
                    var sg = seg();
                    if (s.FrameGraph.Length > 0) { sg.Bars = s.FrameGraph; sg.BarColor = info.Color; }
                    sg.Parts.Add(new Part { Text = double.IsNaN(s.FrameTime) ? "-- ms" : s.FrameTime.ToString("0.0") + " ms", Template = "88.8 ms", Color = double.IsNaN(s.FrameTime) ? dim : val, Bold = !double.IsNaN(s.FrameTime) });
                    segs.Add(sg);
                    break;
                }
                case "Latency": add(double.IsNaN(s.Latency) ? "-- ms" : s.Latency.ToString("0") + " ms", "888 ms", double.IsNaN(s.Latency) ? dim : val); break;
                case "GpuHotspot": addTemp(s.GpuHotspot, c.WarnGpuTemp + 15); break;
                case "GpuMemTemp": addTemp(s.GpuMemTemp, 100); break;
                case "SsdTemp": addTemp(s.SsdTemp, 70); break;
                case "RamTemp": addTemp(s.RamTemp, 85); break;
                case "NetApp":
                    if (s.NetAppRate < 0) add("--", "", dim);
                    else
                    {
                        var sg = seg();
                        string n = s.NetAppName.Length > 12 ? s.NetAppName.Substring(0, 11) + "…" : s.NetAppName;
                        if (n != "") sg.Parts.Add(new Part { Text = n, Template = "", Color = dim });
                        sg.Parts.Add(new Part { Text = Speed(s.NetAppRate), Template = "88.8 MB/s", Color = val, Bold = true });
                        segs.Add(sg);
                    }
                    break;
                case "PublicIp": add(s.PublicIp != "" ? s.PublicIp : "--", "", s.PublicIp != "" ? val : dim); break;
                case "Vpn": if (s.Vpn != null) add(s.Vpn != "" ? "on" : "off", "off", s.Vpn != "" ? val : dim); break;
                case "Clock": add(DateTime.Now.ToString("t"), "", val); break;
                case "Date": add(DateTime.Now.ToString("ddd d MMM"), "", val); break;
                case "Disk": if (s.Disk >= 0) add(s.Disk.ToString("0") + "%", "100%", warn(s.Disk, 95)); break;
                case "DiskRead": if (s.DiskRead >= 0) add(Speed(s.DiskRead), "88.8 MB/s", val); break;
                case "DiskWrite": if (s.DiskWrite >= 0) add(Speed(s.DiskWrite), "88.8 MB/s", val); break;
                case "DiskFree": if (s.FreeGb >= 0) add(SizeGb(s.FreeGb), "888 GB", s.FreePct <= c.WarnFreePct ? c.WarnColor : val); break;
                case "Processes": if (s.Processes >= 0) add(s.Processes.ToString(), "888", val); break;
                case "Uptime": add(Duration(s.Uptime), "88d 88h", val); break;
                case "PcBattery":
                {
                    var ps = SystemInformation.PowerStatus;
                    if (ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryLifePercent <= 1)
                    {
                        int p = (int)Math.Round(ps.BatteryLifePercent * 100);
                        bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
                        var sg = seg();
                        if (!customIcon) sg.Icon = (g, r, cc) => Icons.Battery(g, r, cc, p);
                        sg.Parts.Add(new Part { Text = (charging ? "+" : "") + p + "%", Template = "+100%", Color = p <= c.WarnBattery ? c.WarnColor : val, Bold = true });
                        segs.Add(sg);
                    }
                    break;
                }
                case "BatTime":
                {
                    var ps = SystemInformation.PowerStatus;
                    if (ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.PowerLineStatus != PowerLineStatus.Online && ps.BatteryLifeRemaining > 0)
                        add(Duration(ps.BatteryLifeRemaining), "88h 88m", val);
                    break;
                }
                case "Batteries":
                    foreach (var d in Sampler.Devices)
                    {
                        int p = d.Value;
                        var sg = seg();
                        if (!customIcon) sg.Icon = Icons.ForDevice(d.Key, p);
                        if (p <= c.WarnBattery) sg.IconColor = c.WarnColor;
                        if (c.DeviceNames) sg.Parts.Add(new Part { Text = d.Key.Length > 14 ? d.Key.Substring(0, 13).TrimEnd() + "…" : d.Key, Template = "", Color = dim });
                        sg.Parts.Add(new Part { Text = p + "%", Template = "100%", Color = p <= c.WarnBattery ? c.WarnColor : val, Bold = true });
                        segs.Add(sg);
                    }
                    break;
                default: // a divider: skipped at the start and when two end up next to each other
                    if (segs.Count > 0 && !segs[segs.Count - 1].IsSep)
                    {
                        var col = c.ColorOf(id);
                        segs.Add(new Seg { IsSep = true, IconColor = col.HasValue ? col.Value : dim });
                    }
                    break;
            }
        }
        while (segs.Count > 0 && segs[segs.Count - 1].IsSep) segs.RemoveAt(segs.Count - 1);
        return segs;
    }

    public float FontPx(int height)
    {
        if (Cfg.FontSize > 0) return Cfg.FontSize * 96f / 72f * DeviceDpi / 96f;
        return Math.Max(11, Math.Min(height * (Cfg.TwoLines ? 0.27f : 0.3f), 20));
    }

    static bool lightCache; static DateTime lightAt;
    public static bool LightTaskbar()
    {
        if ((DateTime.UtcNow - lightAt).TotalSeconds < 3) return lightCache;
        lightAt = DateTime.UtcNow;
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                lightCache = k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
        }
        catch { lightCache = false; }
        return lightCache;
    }

    // ---- Cached drawing resources (fonts, measuring surface, brush), rebuilt only when the font changes.
    Font font, bold; string fontKey; float lineH;
    Bitmap measureBmp; Graphics measureG;
    readonly SolidBrush brush = new SolidBrush(Color.White);
    readonly Dictionary<string, float> templateW = new Dictionary<string, float>();
    readonly StringBuilder keyBuf = new StringBuilder();
    static readonly StringFormat Fmt = MakeFormat();

    static StringFormat MakeFormat()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    void EnsureFonts(float px)
    {
        var key = Cfg.FontName + "|" + px;
        if (key == fontKey) return;
        DisposeFonts();
        if (measureG == null) { measureBmp = new Bitmap(1, 1); measureG = Graphics.FromImage(measureBmp); }
        font = new Font(Cfg.FontName, px, FontStyle.Regular, GraphicsUnit.Pixel);
        bold = new Font(Cfg.FontName, px, FontStyle.Bold, GraphicsUnit.Pixel);
        lineH = font.GetHeight(measureG);
        templateW.Clear();
        fontKey = key;
    }

    void DisposeFonts()
    {
        if (font != null) font.Dispose();
        if (bold != null) bold.Dispose();
        font = bold = null; fontKey = null;
    }

    float Measure(string text, Font f) { return measureG.MeasureString(text, f, PointF.Empty, Fmt).Width; }

    float TemplateWidth(string text, bool isBold)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var key = (isBold ? "b" : "r") + text;
        float w;
        if (!templateW.TryGetValue(key, out w)) templateW[key] = w = Measure(text, isBold ? bold : font);
        return w;
    }

    // Works out what goes where. Key sums up everything that affects the pixels, so unchanged frames can be skipped.
    public BarLayout Build(int height, bool light)
    {
        Color val = Cfg.ValueColor, dim = Cfg.LabelColor;
        if (Cfg.AutoTheme)
        {
            val = light ? Color.FromArgb(20, 20, 20) : Color.White;
            dim = light ? Color.FromArgb(95, 95, 100) : Color.FromArgb(150, 150, 160);
        }
        var segs = BuildSegments(Sampler.Snap, val, dim, Cfg.OrderList(), Cfg.IsOn, false);
        if (segs.Count == 0) segs.Add(new Seg { Icon = Icons.Gear, IconColor = dim, Parts = { new Part { Text = "Right-click for settings", Template = "", Color = dim } } });

        float fontPx = FontPx(height);
        EnsureFonts(fontPx);
        var L = new BarLayout { FontPx = fontPx, LineH = lineH };
        L.Icon = (float)Math.Round(fontPx * 1.15f * Cfg.IconScale / 100f);
        L.IconGap = fontPx * 0.4f * Cfg.GapIcon / 100f;
        L.PartGap = fontPx * 0.45f;
        L.SegGap = fontPx * 1.1f * Cfg.GapStats / 100f;
        L.Pad = fontPx * 0.9f * Cfg.EdgePad / 100f;
        L.BarW = Math.Max(2, fontPx * 0.22f); L.BarGap = Math.Max(1, L.BarW * 0.45f);
        L.RowH = Math.Max(lineH, L.Icon);

        foreach (var seg in segs)
        {
            if (seg.IsSep)
            {
                // Keep a little room either side of a divider even when "Between stats" is set very tight.
                seg.Mark = Cfg.DividerStyle == 2 ? Math.Max(3, fontPx * 0.28f) : Cfg.DividerStyle == 3 ? 0 : 2;
                seg.W = seg.Mark + 2 * Math.Max(0, fontPx * 0.35f - L.SegGap);
                continue;
            }
            float w = 0;
            if (seg.Label != null) { seg.LabelW = Measure(seg.Label, bold); w += seg.LabelW + L.IconGap; }
            else if (seg.Icon != null) w += L.Icon + L.IconGap;
            if (seg.Bars != null) w += seg.Bars.Length * (L.BarW + L.BarGap) - L.BarGap;
            for (int i = 0; i < seg.Parts.Count; i++)
            {
                var p = seg.Parts[i];
                p.W = Math.Max(Measure(p.Text, p.Bold ? bold : font), TemplateWidth(p.Template, p.Bold));
                w += p.W + (i > 0 ? L.PartGap : 0);
            }
            seg.W = (float)Math.Ceiling(w);
        }

        // One segment per column, or two stacked in two-line mode. Dividers always get a column of their own.
        foreach (var seg in segs)
        {
            var last = L.Cols.Count > 0 ? L.Cols[L.Cols.Count - 1] : null;
            if (Cfg.TwoLines && !seg.IsSep && last != null && last.Count == 1 && !last[0].IsSep) last.Add(seg);
            else L.Cols.Add(new List<Seg> { seg });
        }
        L.Width = Math.Max(4, (int)Math.Ceiling(L.Pad * 2 + L.Cols.Sum(col => col.Max(sg => sg.W)) + L.SegGap * (L.Cols.Count - 1)));

        var k = keyBuf; k.Clear();
        k.Append(L.Width).Append('x').Append(height).Append(light ? 'L' : 'D');
        foreach (var seg in segs)
        {
            k.Append('|').Append(seg.IconColor.ToArgb()).Append(seg.Label);
            foreach (var p in seg.Parts) k.Append(';').Append(p.Text).Append(p.Color.ToArgb());
            if (seg.Bars != null) foreach (var b in seg.Bars) k.Append(',').Append((int)b);
        }
        L.Key = k.ToString();
        return L;
    }

    public void PaintBar(Graphics g, BarLayout L, int height)
    {
        g.Clear(Color.FromArgb(1, 0, 0, 0)); // near-invisible, but still catches mouse clicks
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float fontPx = L.FontPx, icon = L.Icon, rowH = L.RowH;

        if (Cfg.Pill && Cfg.PillOpacity > 0)
        {
            float ph = Cfg.TwoLines ? height - 6 : Math.Min(height - 8, fontPx * 2.3f);
            var pr = new RectangleF(1, (height - ph) / 2, L.Width - 2, ph);
            float rad = Math.Min(ph / 2, Math.Min(ph / 2, fontPx * 0.8f) * Cfg.PillRound / 100f);
            using (var path = Icons.Round(pr, rad))
            {
                brush.Color = Color.FromArgb((int)(Cfg.PillOpacity * 2.55), Cfg.PillColor);
                g.FillPath(brush, path);
            }
        }

        float cx = L.Pad;
        foreach (var col in L.Cols)
        {
            float colW = col.Max(sg => sg.W);
            for (int row = 0; row < col.Count; row++)
            {
                var seg = col[row];
                if (seg.IsSep) { PaintDivider(g, L, seg, cx, height); continue; }
                float cy = col.Count == 1 ? (height - rowH) / 2 : (row == 0 ? height / 2f - rowH - Cfg.RowGap / 2f : height / 2f + Cfg.RowGap / 2f);
                seg.X = cx; seg.Y = col.Count == 1 ? 0 : cy;
                float sx = cx;
                if (seg.Label != null)
                {
                    brush.Color = seg.IconColor;
                    g.DrawString(seg.Label, bold, brush, sx, cy + (rowH - L.LineH) / 2, Fmt);
                    sx += seg.LabelW + L.IconGap;
                }
                else if (seg.Icon != null)
                {
                    var ir = new RectangleF(sx, cy + (rowH - icon) / 2, icon, icon);
                    if (Cfg.BarAnimate) anim.Draw(g, seg.Id, seg.Icon, ir, seg.IconColor, seg.Act, seg.IconColor == Cfg.WarnColor);
                    else seg.Icon(g, ir, seg.IconColor);
                    sx += icon + L.IconGap;
                }
                if (seg.Bars != null)
                {
                    float by = cy + (rowH - icon) / 2;
                    using (var bg = new SolidBrush(Color.FromArgb(55, seg.BarColor))) using (var fg = new SolidBrush(seg.BarColor))
                        for (int i = 0; i < seg.Bars.Length; i++)
                        {
                            float x = sx + i * (L.BarW + L.BarGap), h = icon * (float)seg.Bars[i] / 100f;
                            g.FillRectangle(bg, x, by, L.BarW, icon);
                            g.FillRectangle(fg, x, by + icon - h, L.BarW, h);
                        }
                }
                for (int i = 0; i < seg.Parts.Count; i++)
                {
                    var p = seg.Parts[i];
                    if (i > 0) sx += L.PartGap;
                    brush.Color = p.Color;
                    g.DrawString(p.Text, p.Bold ? bold : font, brush, sx, cy + (rowH - L.LineH) / 2, Fmt);
                    sx += p.W;
                }
            }
            cx += colW + L.SegGap;
        }
    }

    void PaintDivider(Graphics g, BarLayout L, Seg seg, float x, int height)
    {
        float sh = Math.Min(height - 4, (Cfg.TwoLines ? height * 0.62f : L.Icon * 1.5f) * Cfg.DividerHeight / 100f);
        float mid = x + seg.W / 2, top = (height - sh) / 2;
        var col = Color.FromArgb(110, seg.IconColor);
        switch (Cfg.DividerStyle)
        {
            case 0:
                using (var p = new Pen(col, 1.5f)) g.DrawLine(p, mid, top, mid, top + sh);
                break;
            case 1:
                using (var p = new Pen(col, 1.6f) { DashCap = DashCap.Round, DashPattern = new[] { 1f, 2f } }) g.DrawLine(p, mid, top, mid, top + sh);
                break;
            case 2:
                brush.Color = Color.FromArgb(160, seg.IconColor);
                g.FillEllipse(brush, mid - seg.Mark / 2, height / 2f - seg.Mark / 2, seg.Mark, seg.Mark);
                break;
        }
    }

    // Draws the bar into a new bitmap (used by the settings preview).
    public Bitmap RenderBitmap(int height, bool light)
    {
        var L = Build(height, light);
        var bmp = new Bitmap(L.Width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) PaintBar(g, L, height);
        return bmp;
    }

    string lastKey; int lastX = int.MinValue, lastY;

    public void Render() { Render(false); }

    public void Render(bool force)
    {
        int qs;
        bool fullscreen = Cfg.HideFullscreen && SHQueryUserNotificationState(out qs) == 0 && (qs == 2 || qs == 3 || qs == 4);
        // Only hide what shares a monitor with the fullscreen app, so the other screens keep their stats.
        Screen fsScreen = null;
        if (fullscreen) { var fg = GetForegroundWindow(); if (fg != IntPtr.Zero) fsScreen = Screen.FromHandle(fg); }
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        SyncWidget();
        if (widget != null) { if (fullscreen && (fsScreen == null || widget.IsOnScreen(fsScreen))) widget.HideNow(); else widget.Render(force); }
        var snap = Sampler.Snap;
        bool freshSnap = snap != watched;
        if (freshSnap) { watched = snap; Watch.OnSample(snap); RecordHover(snap); }
        tip.Active = !Cfg.BarHover;
        if (!Cfg.BarHover && hover != null && hover.Visible) HideHover();
        SyncMirrors(fullscreen, fsScreen);
        SyncOverlay();
        if (overlay != null) overlay.Render(force); // made for games, so it stays up in fullscreen
        SyncHotkey();
        SyncTaskbarButton();
        SyncTray();
        UpdateTrayText(Sampler.Snap);

        RECT tb;
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out tb)) return;
        bool hideBar = fullscreen && (fsScreen == null || Screen.FromHandle(taskbar).DeviceName == fsScreen.DeviceName);
        if (hideBar) { ShowWindow(Handle, 0); shownL = null; return; }

        TaskbarHeight = tb.B - tb.T;
        var L = Build(TaskbarHeight, LightTaskbar());
        int right;
        IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        RECT tr;
        if (tray != IntPtr.Zero && GetWindowRect(tray, out tr) && tr.L > tb.L) right = tr.L - 4;
        else right = tb.R - (int)(TaskbarHeight * 5.5);
        int x = right - Cfg.Offset - L.Width, y = tb.T;
        // Most ticks change nothing visible (e.g. idle network), so skip the repaint entirely.
        if (force || shownL == null || L.Key != lastKey || x != lastX || y != lastY)
        {
            Push(L, TaskbarHeight, x, y);
            lastKey = L.Key; lastX = x; lastY = y;
        }
        UpdateTooltip(Sampler.Snap);
        ShowWindow(Handle, 8); // SW_SHOWNA
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // stay above the taskbar
        if (freshSnap && hover != null && hover.Visible) ShowHover();
    }

    // ---- The bar on other monitors' taskbars: one mirror per secondary taskbar, left of where its clock sits.
    readonly List<MirrorBar> mirrors = new List<MirrorBar>();

    void SyncMirrors(bool fullscreen, Screen fsScreen)
    {
        var bars = new List<IntPtr>();
        if (Cfg.AllTaskbars)
            for (IntPtr h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); h != IntPtr.Zero; h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null))
                bars.Add(h);
        while (mirrors.Count > bars.Count) { var m = mirrors[mirrors.Count - 1]; mirrors.RemoveAt(mirrors.Count - 1); m.Close(); m.Dispose(); }
        while (mirrors.Count < bars.Count) { var m = new MirrorBar(this); mirrors.Add(m); }
        bool light = LightTaskbar();
        for (int i = 0; i < bars.Count; i++)
        {
            RECT tb;
            if (!GetWindowRect(bars[i], out tb) || (fullscreen && (fsScreen == null || Screen.FromHandle(bars[i]).DeviceName == fsScreen.DeviceName))) { mirrors[i].HideNow(); continue; }
            int h = tb.B - tb.T;
            if (h <= 0 || tb.R - tb.L < h * 4) { mirrors[i].HideNow(); continue; } // vertical or hidden taskbar
            var L = Build(h, light);
            int x = tb.R - (int)(h * 2.4) - Cfg.Offset - L.Width;
            mirrors[i].Push(L, h, x, tb.T);
        }
    }

    void UpdateTooltip(Snapshot s)
    {
        var sb = new StringBuilder();
        if (s.CpuName != "") sb.AppendLine("CPU: " + s.CpuName);
        if (s.CpuTempStatus != "" && (Cfg.CpuTemp || Cfg.CpuPower)) sb.AppendLine("CPU temp: " + s.CpuTempStatus);
        if (s.HasGpu) sb.AppendLine("GPU: " + s.GpuName);
        if (Cfg.Fps) sb.AppendLine("FPS: " + (s.FpsStatus != "" ? s.FpsStatus : s.FpsApp != "" ? s.FpsApp : "the app in front"));
        if (Cfg.Ping) sb.AppendLine("Ping: " + Cfg.PingHost);
        if (Cfg.Wifi && s.WifiName != "") sb.AppendLine("Wi-Fi: " + s.WifiName);
        if (Cfg.DiskFree && s.FreeName != "") sb.AppendLine("Free space: " + s.FreeName + " (" + s.FreePct.ToString("0") + "% free)");
        foreach (var d in Sampler.Devices) sb.AppendLine(d.Key + ": " + d.Value + "%");
        sb.Append("Left-click: Task Manager  ·  Right-click: Settings");
        var text = sb.ToString();
        if (text != lastTip) { lastTip = text; tip.SetToolTip(this, text); }
    }

    // ---- The bar is drawn straight into one reusable DIB, instead of allocating a bitmap + HBITMAP every tick.
    readonly DibSurface surface = new DibSurface();

    void Push(BarLayout L, int height, int x, int y)
    {
        var bmp = surface.Canvas(L.Width, height);
        using (var g = Graphics.FromImage(bmp)) PaintBar(g, L, height);
        surface.Push(Handle, x, y);
        shownL = L; shownH = height;
    }

    // ---- Icon animation: the last layout is repainted every frame so the icons keep moving between samples.
    readonly IconAnim anim = new IconAnim();
    BarLayout shownL; int shownH;

    void AnimFrame()
    {
        anim.Advance();
        SpinTray();
        if (Cfg.BarAnimate && shownL != null && IsHandleCreated && lastX != int.MinValue) Push(shownL, shownH, lastX, lastY);
        if (Cfg.BarAnimate) foreach (var m in mirrors) if (m.Last != null) m.Push(m.Last, m.H, m.X, m.Y);
    }

    // ---- Desktop widget: created and destroyed to match the settings.
    WidgetForm widget;

    void SyncWidget()
    {
        if (widget != null && (!Cfg.WidgetShow || widget.Layer != Cfg.WidgetLayer)) { widget.Close(); widget.Dispose(); widget = null; }
        if (Cfg.WidgetShow && widget == null) { widget = new WidgetForm(this); widget.Show(); }
    }

    public void SetWidget(bool on)
    {
        Cfg.WidgetShow = on;
        Cfg.Save();
        BeginInvoke((Action)(() => { Render(true); NotifySettings("Widget"); }));
    }

    // ---- Game overlay, plus a global hotkey to show or hide it without leaving the game.
    OverlayForm overlay;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    bool hotkeyOn;

    void SyncOverlay()
    {
        bool show = Cfg.OverlayShow || Watch.AutoOverlay;
        if (overlay != null && !show) { overlay.Close(); overlay.Dispose(); overlay = null; }
        if (show && overlay == null) { overlay = new OverlayForm(this); overlay.Show(); }
    }

    public void SetOverlay(bool on)
    {
        Cfg.OverlayShow = on;
        Cfg.Save();
        BeginInvoke((Action)(() => { Render(true); NotifySettings("Overlay"); }));
    }

    bool benchKeyOn;

    // Ctrl+Shift+F10 shows or hides the overlay, Ctrl+Shift+F11 starts or stops a benchmark recording.
    void SyncHotkey()
    {
        if (!IsHandleCreated) return;
        SyncKey(1, Keys.F10, Cfg.OverlayHotkey, ref hotkeyOn);
        SyncKey(2, Keys.F11, Cfg.BenchHotkey, ref benchKeyOn);
        if (Cfg.OverlayHotkey && !hotkeyOn) Cfg.OverlayHotkey = false; // another app already has it
        if (Cfg.BenchHotkey && !benchKeyOn) Cfg.BenchHotkey = false;
    }

    void SyncKey(int id, Keys key, bool want, ref bool on)
    {
        if (want == on) return;
        if (want) on = RegisterHotKey(Handle, id, 0x2 | 0x4 | 0x4000, (uint)key); // Ctrl+Shift, no auto-repeat
        else { UnregisterHotKey(Handle, id); on = false; }
    }

    public void ToggleOverlay()
    {
        if (!Cfg.OverlayShow && Watch.AutoOverlay) { Watch.Suppress(); Render(true); } // hides it for this game
        else SetOverlay(!Cfg.OverlayShow);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312) // WM_HOTKEY
        {
            if (m.WParam.ToInt32() == 1) ToggleOverlay();
            else if (m.WParam.ToInt32() == 2) Watch.ToggleRecording();
            return;
        }
        base.WndProc(ref m);
    }

    // ---- Notifications, as Windows toasts through the tray icon (a temporary one when the tray icon is off).
    NotifyIcon noteIcon; Action noteClick;
    readonly System.Windows.Forms.Timer noteTimer = new System.Windows.Forms.Timer { Interval = 15000 };

    public void Notify(string title, string text, Action click)
    {
        var icon = tray;
        if (icon == null)
        {
            if (noteIcon == null)
            {
                noteIcon = new NotifyIcon { Icon = TrayIconArt.Frame(Cfg.TrayStyle, SystemInformation.SmallIconSize.Width, 0), Text = "Kinetik" };
                noteIcon.BalloonTipClicked += (s, e) => { if (noteClick != null) noteClick(); };
                noteTimer.Tick += (s, e) => { noteTimer.Stop(); if (noteIcon != null) { noteIcon.Visible = false; noteIcon.Dispose(); noteIcon = null; } };
            }
            noteIcon.Visible = true;
            noteTimer.Stop(); noteTimer.Start();
            icon = noteIcon;
        }
        noteClick = click;
        try { icon.ShowBalloonTip(8000, title, text, ToolTipIcon.None); } catch { }
    }

    // Keeps an open Settings page in step with changes made from a right-click menu.
    public void NotifySettings(string page)
    {
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshIfShowing(page);
    }
}

// ======================================================================= Safe file writes
// Session logs, benchmarks and profiles are written while Kinetik may be running as administrator, into folders
// the user's own programs can change. Refusing links stops one of those programs redirecting an admin write onto
// a protected file.
static class SafeFile
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)] // FILETIMEs are two DWORDs, so 4-byte aligned
    struct FileInfoByHandle
    {
        public uint Attributes; public long Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out FileInfoByHandle info);

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    struct FindData
    {
        public uint Attributes; public long Created, Accessed, Written; public uint SizeHigh, SizeLow, ReparseTag, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string ShortName;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindFirstFileW(string path, out FindData data);
    [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);

    // A symbolic link or junction. Other reparse points, like OneDrive's cloud folders, are ordinary folders here.
    static bool IsLink(string path)
    {
        FindData d;
        var h = FindFirstFileW(path, out d);
        if (h == new IntPtr(-1)) return false;
        FindClose(h);
        return (d.Attributes & 0x400) != 0 && (d.ReparseTag == 0xA000000C || d.ReparseTag == 0xA0000003); // SYMLINK, MOUNT_POINT
    }

    // Opens path for appending (or creating), as UTF-8. Throws if the file or its folder is a link.
    public static StreamWriter Append(string path)
    {
        var dir = Path.GetDirectoryName(path);
        Directory.CreateDirectory(dir);
        for (var d = new DirectoryInfo(dir); d != null && d.Parent != null; d = d.Parent)
            if (IsLink(d.FullName)) throw new IOException(d.FullName + " is a link");
        if (File.Exists(path) && IsLink(path)) throw new IOException(path + " is a link");
        var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        FileInfoByHandle info;
        if (!GetFileInformationByHandle(fs.SafeFileHandle, out info) || info.Links > 1) // a hard link to another file
        {
            fs.Dispose();
            throw new IOException(path + " is linked elsewhere");
        }
        fs.Seek(0, SeekOrigin.End);
        return new StreamWriter(fs, new UTF8Encoding(false));
    }

    public static void Write(string path, string text)
    {
        using (var w = Append(path)) { w.BaseStream.SetLength(0); w.Write(text); }
    }
}

// ======================================================================= Game sessions, benchmarks and alerts
// Runs on the UI thread with each new sample: shows the overlay while a game is in front, keeps a summary of each
// game session, records benchmarks, and raises notifications when something needs attention.
class GameWatch
{
    readonly StatsBar bar;
    Settings c { get { return bar.Cfg; } }
    public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kinetik"); } }

    public GameWatch(StatsBar bar) { this.bar = bar; }

    // ---- overlay while gaming
    public bool AutoOverlay { get; private set; }
    int suppressedPid;
    DateTime lastGameAt = DateTime.MinValue;

    // The hotkey hides an automatically shown overlay until that game closes.
    public void Suppress() { suppressedPid = cur != null ? cur.Pid : -1; AutoOverlay = false; }

    // ---- sessions
    class Session
    {
        public int Pid; public string Name; public DateTime Start, LastSeen;
        public double FpsSum, LowSum; public int FpsCount, LowCount;
        public double PeakCpu = double.NaN, PeakGpu = double.NaN, PeakHot = double.NaN;
    }
    Session cur;
    DateTime exitCheckAt;

    static double Max(double a, double b) { return double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b); }

    public void OnSample(Snapshot s)
    {
        var now = DateTime.UtcNow;
        if (s.GamePid != 0)
        {
            lastGameAt = now;
            if (c.OverlayAuto && s.GamePid != suppressedPid) AutoOverlay = true;
            if (cur == null || cur.Pid != s.GamePid)
            {
                if (cur != null) End(cur);
                cur = new Session { Pid = s.GamePid, Name = s.GameName, Start = now };
            }
            cur.LastSeen = now;
            if (!double.IsNaN(s.Fps)) { cur.FpsSum += s.Fps; cur.FpsCount++; }
            if (!double.IsNaN(s.FpsLow)) { cur.LowSum += s.FpsLow; cur.LowCount++; }
            cur.PeakCpu = Max(cur.PeakCpu, s.CpuTemp);
            if (s.HasGpu) cur.PeakGpu = Max(cur.PeakGpu, s.GpuTemp);
            cur.PeakHot = Max(cur.PeakHot, s.GpuHotspot);
        }
        else if (AutoOverlay && (now - lastGameAt).TotalSeconds > 5) AutoOverlay = false;
        if (!c.OverlayAuto) AutoOverlay = false;

        // A session ends when its game closes.
        if (cur != null && (now - exitCheckAt).TotalSeconds >= 5)
        {
            exitCheckAt = now;
            bool alive;
            try { using (var p = Process.GetProcessById(cur.Pid)) alive = !p.HasExited; }
            catch (ArgumentException) { alive = false; }
            catch { alive = true; } // not allowed to ask: assume it's still running
            if (!alive) { End(cur); cur = null; suppressedPid = 0; }
        }

        if (writer != null) Record(s);
        Alerts(s, now);
    }

    void End(Session x)
    {
        var length = x.LastSeen - x.Start;
        if (!c.SessionSummary || length.TotalSeconds < 60 || x.FpsCount == 0) return;
        double avg = x.FpsSum / x.FpsCount, low = x.LowCount > 0 ? x.LowSum / x.LowCount : double.NaN;
        var ci = CultureInfo.InvariantCulture;
        Func<double, string> n = v => double.IsNaN(v) ? "" : v.ToString("0", ci);
        try
        {
            string path = Path.Combine(DataDir, "Sessions.csv");
            bool fresh = !File.Exists(path);
            using (var w = SafeFile.Append(path))
            {
                if (fresh) w.WriteLine("Start,Game,Minutes,Average FPS,1% low FPS,Peak CPU °C,Peak GPU °C,Peak GPU hot spot °C");
                w.WriteLine(string.Join(",", x.Start.ToLocalTime().ToString("yyyy-MM-dd HH:mm", ci), Csv(x.Name), length.TotalMinutes.ToString("0", ci),
                    n(avg), n(low), n(x.PeakCpu), n(x.PeakGpu), n(x.PeakHot)));
            }
        }
        catch { }
        var parts = new List<string> { StatsBar.Duration(length.TotalSeconds), "average " + avg.ToString("0") + " fps" };
        if (!double.IsNaN(low)) parts.Add("1% low " + low.ToString("0"));
        if (!double.IsNaN(x.PeakGpu)) parts.Add("GPU peak " + x.PeakGpu.ToString("0") + "°C");
        bar.Notify("Game session: " + x.Name, string.Join(" · ", parts), () => OpenFolder(DataDir));
    }

    static string Csv(string v) { return v.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v; }

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + path + "\"")); }
        catch { }
    }

    // ---- benchmark recording: every sample to a CSV file in Documents\Kinetik Benchmarks
    StreamWriter writer; string recordPath; int rows; double recFpsSum; int recFpsCount;
    public bool IsRecording { get { return writer != null; } }

    public void ToggleRecording()
    {
        if (writer != null) { StopRecording(); return; }
        var s = bar.Sampler.Snap;
        string app = s.GameName != "" ? s.GameName : s.FpsApp != "" ? s.FpsApp : "Kinetik";
        app = new string(app.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
        if (app == "") app = "Kinetik";
        recordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kinetik Benchmarks",
            app + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".csv");
        try
        {
            writer = SafeFile.Append(recordPath);
            writer.WriteLine("Time,App,FPS,Base FPS,1% low FPS,0.1% low FPS,Frame time ms,Latency ms,CPU %,CPU MHz,CPU °C,CPU W," +
                             "GPU %,GPU °C,GPU hot spot °C,GPU MHz,GPU W,VRAM GB,RAM %,RAM GB");
        }
        catch (Exception e) { writer = null; bar.Notify("Couldn't start recording", e.Message, null); return; }
        rows = 0; recFpsSum = 0; recFpsCount = 0;
        c.Recording = true;
        bar.Notify("Recording benchmark", (c.BenchHotkey ? "Press Ctrl+Shift+F11 again to stop." : "Use the Kinetik menu to stop.") + (Program.IsAdmin ? "" : " FPS needs admin."), null);
    }

    void Record(Snapshot s)
    {
        var ci = CultureInfo.InvariantCulture;
        Func<double, string, string> n = (v, f) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString(f, ci);
        try
        {
            writer.WriteLine(string.Join(",", DateTime.Now.ToString("HH:mm:ss.f", ci), Csv(s.GameName != "" ? s.GameName : s.FpsApp),
                n(s.Fps, "0.0"), n(s.BaseFps, "0.0"), n(s.FpsLow, "0.0"), n(s.FpsLow01, "0.0"), n(s.FrameTime, "0.00"), n(s.Latency, "0.0"),
                n(s.Cpu, "0"), n(s.CpuMhz, "0"), n(s.CpuTemp, "0"), n(s.CpuPower, "0.0"),
                s.HasGpu ? n(s.GpuUtil, "0") : "", s.HasGpu ? n(s.GpuTemp, "0") : "", n(s.GpuHotspot, "0"),
                s.HasGpu ? n(s.GpuMhz, "0") : "", s.HasGpu ? n(s.GpuPower, "0.0") : "", s.HasGpu ? n(s.VramUsed, "0.00") : "",
                s.RamLoad.ToString(ci), n(s.RamUsed, "0.00")));
            rows++;
            if (!double.IsNaN(s.Fps)) { recFpsSum += s.Fps; recFpsCount++; }
            if (rows % 10 == 0) writer.Flush();
        }
        catch { StopRecording(); }
    }

    public void StopRecording()
    {
        if (writer == null) return;
        try { writer.Dispose(); } catch { }
        writer = null;
        c.Recording = false;
        var path = recordPath;
        string text = rows + " samples" + (recFpsCount > 0 ? ", average " + (recFpsSum / recFpsCount).ToString("0") + " fps" : "") + ". Click to open the folder.";
        bar.Notify("Benchmark saved", text, () => OpenFolder(Path.GetDirectoryName(path)));
    }

    // ---- alerts: a notification when something has needed attention for a while, then quiet for a cooldown
    DateTime cpuHotSince = DateTime.MaxValue, gpuHotSince = DateTime.MaxValue, pingBadSince = DateTime.MaxValue;
    readonly Dictionary<string, DateTime> lastAlert = new Dictionary<string, DateTime>();
    readonly HashSet<string> lowDevices = new HashSet<string>();
    bool pcLow;

    bool Due(string key, double minutes, DateTime now)
    {
        DateTime t;
        if (lastAlert.TryGetValue(key, out t) && (now - t).TotalMinutes < minutes) return false;
        lastAlert[key] = now;
        return true;
    }

    static bool Held(ref DateTime since, bool bad, DateTime now, double secs)
    {
        if (!bad) { since = DateTime.MaxValue; return false; }
        if (since == DateTime.MaxValue) since = now;
        return (now - since).TotalSeconds >= secs;
    }

    void Alerts(Snapshot s, DateTime now)
    {
        if (c.AlertTemps)
        {
            if (Held(ref cpuHotSince, !double.IsNaN(s.CpuTemp) && s.CpuTemp >= c.WarnCpuTemp, now, 30) && Due("cpu", 10, now))
                bar.Notify("CPU is running hot", s.CpuTemp.ToString("0") + "°C for the last 30 seconds (your limit is " + c.WarnCpuTemp + "°C).", null);
            if (Held(ref gpuHotSince, s.HasGpu && s.GpuTemp >= c.WarnGpuTemp, now, 30) && Due("gpu", 10, now))
                bar.Notify("GPU is running hot", s.GpuTemp.ToString("0") + "°C for the last 30 seconds (your limit is " + c.WarnGpuTemp + "°C).", null);
        }
        if (c.AlertPing && Held(ref pingBadSince, s.Ping == -2 || s.Ping >= c.WarnPing, now, 30) && Due("ping", 10, now))
            bar.Notify(s.Ping == -2 ? "Ping is timing out" : "Ping is high", (s.Ping == -2 ? "No replies" : s.Ping + " ms") + " from " + c.PingHost + " for the last 30 seconds.", null);
        if (c.AlertDisk && s.FreeGb >= 0 && s.FreePct <= c.WarnFreePct && Due("disk", 6 * 60, now))
            bar.Notify("Drive almost full", s.FreeName + " has " + StatsBar.SizeGb(s.FreeGb) + " free (" + s.FreePct.ToString("0") + "%).", null);
        if (c.AlertBattery)
        {
            foreach (var d in bar.Sampler.Devices)
            {
                if (d.Value <= c.WarnBattery && lowDevices.Add(d.Key)) bar.Notify("Battery low: " + d.Key, d.Key + " is at " + d.Value + "%.", null);
                else if (d.Value > c.WarnBattery + 5) lowDevices.Remove(d.Key); // charged again: warn next time
            }
            var ps = SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryLifePercent <= 1)
            {
                int p = (int)Math.Round(ps.BatteryLifePercent * 100);
                bool low = p <= c.WarnBattery && ps.PowerLineStatus != PowerLineStatus.Online;
                if (low && !pcLow) bar.Notify("Battery low", "Your PC is at " + p + "%. Plug it in soon.", null);
                if (!low && (p > c.WarnBattery + 5 || ps.PowerLineStatus == PowerLineStatus.Online)) pcLow = false; else if (low) pcLow = true;
            }
        }
    }
}

// ======================================================================= Hover graphs
// The processes using the most of something, sampled once a second while a hover graph shows them.
static class TopProcs
{
    [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr h, out IoCounters c);
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }

    static Dictionary<int, long> cpuPrev = new Dictionary<int, long>(), ioPrev = new Dictionary<int, long>();
    static Dictionary<string, CounterSample> gpuPrev = new Dictionary<string, CounterSample>();
    static DateTime prevAt = DateTime.MinValue;
    static string kindNow = "";
    static int busy;
    public static volatile List<KeyValuePair<string, double>> Result = new List<KeyValuePair<string, double>>();
    static readonly Regex GpuInstance = new Regex(@"pid_(\d+)_.*engtype_(.+)$");

    // kind: "cpu" (% of all cores), "ram" (bytes in use), "disk" (bytes/s), "gpu" (% of the busiest engine)
    public static void Sample(string kind)
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ => { try { Run(kind); } catch { } finally { busy = 0; } });
    }

    public static void Reset() { kindNow = ""; Result = new List<KeyValuePair<string, double>>(); }

    static void Run(string kind)
    {
        if (kind != kindNow)
        {
            cpuPrev.Clear(); ioPrev.Clear(); gpuPrev.Clear(); prevAt = DateTime.MinValue;
            kindNow = kind; Result = new List<KeyValuePair<string, double>>();
        }
        var now = DateTime.UtcNow;
        double secs = prevAt == DateTime.MinValue ? 0 : (now - prevAt).TotalSeconds;
        prevAt = now;
        var totals = new Dictionary<string, double>();
        Action<string, double> add = (n, v) => { double t; totals.TryGetValue(n, out t); totals[n] = t + v; };

        if (kind == "gpu")
        {
            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["utilization percentage"];
            var next = new Dictionary<string, CounterSample>();
            var perPid = new Dictionary<int, Dictionary<string, double>>();
            foreach (InstanceData d in data.Values)
            {
                next[d.InstanceName] = d.Sample;
                CounterSample old;
                var m = GpuInstance.Match(d.InstanceName);
                if (!m.Success || !gpuPrev.TryGetValue(d.InstanceName, out old)) continue;
                int pid = int.Parse(m.Groups[1].Value);
                Dictionary<string, double> engines;
                if (!perPid.TryGetValue(pid, out engines)) perPid[pid] = engines = new Dictionary<string, double>();
                double e; engines.TryGetValue(m.Groups[2].Value, out e);
                engines[m.Groups[2].Value] = e + CounterSample.Calculate(old, d.Sample);
            }
            gpuPrev = next;
            foreach (var kv in perPid)
            {
                string name;
                try { using (var p = Process.GetProcessById(kv.Key)) name = p.ProcessName; } catch { continue; }
                add(name, kv.Value.Values.Max()); // like Task Manager: the busiest engine
            }
        }
        else
        {
            var nextCpu = new Dictionary<int, long>(); var nextIo = new Dictionary<int, long>();
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    try
                    {
                        if (p.Id == 0) continue; // the Idle process
                        long old;
                        if (kind == "ram") add(p.ProcessName, p.WorkingSet64);
                        else if (kind == "cpu")
                        {
                            long t = p.TotalProcessorTime.Ticks; nextCpu[p.Id] = t;
                            if (secs > 0 && cpuPrev.TryGetValue(p.Id, out old))
                                add(p.ProcessName, (t - old) * 100.0 / (secs * TimeSpan.TicksPerSecond * Environment.ProcessorCount));
                        }
                        else if (kind == "disk")
                        {
                            IoCounters io;
                            if (!GetProcessIoCounters(p.Handle, out io)) continue;
                            long t = (long)(io.ReadBytes + io.WriteBytes); nextIo[p.Id] = t;
                            if (secs > 0 && ioPrev.TryGetValue(p.Id, out old)) add(p.ProcessName, (t - old) / secs);
                        }
                    }
                    catch { } // protected processes can't be asked
                }
            cpuPrev = nextCpu; ioPrev = nextIo;
        }
        Result = totals.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(3).ToList();
    }
}

// The card that opens above a stat on the bar: its last minute as a graph, the range, and what's behind it.
class HoverPopup : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    string title = "", value = "", range = "";
    double[] hist = new double[0];
    List<string> lines = new List<string>();
    Color accent = Theme.Accent;
    Font fTitle, fValue, fSmall;
    float u = 1;

    public HoverPopup()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true; BackColor = Theme.Card;
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8 | 0x08000000 | 0x20; return cp; } // TOOLWINDOW | TOPMOST | NOACTIVATE | TRANSPARENT
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); // rounded corners
        int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var f in new[] { fTitle, fValue, fSmall }) if (f != null) f.Dispose();
        base.Dispose(disposing);
    }

    public void Set(string title, string value, Color accent, double[] hist, string range, List<string> lines, int cursorX, int barTop)
    {
        this.title = title; this.value = value; this.accent = accent; this.hist = hist; this.range = range; this.lines = lines;
        float nu = DeviceDpi / 96f;
        if (fTitle == null || nu != u)
        {
            u = nu;
            foreach (var f in new[] { fTitle, fValue, fSmall }) if (f != null) f.Dispose();
            fTitle = Theme.UI(9.5f, FontStyle.Bold); fValue = Theme.UI(15f, FontStyle.Bold); fSmall = Theme.UI(8.5f);
        }
        int w = (int)(290 * u);
        int h = (int)((12 + 22 + 30 + (hist.Length > 1 ? 62 + 18 : 0) + lines.Count * 18 + 10) * u);
        var wa = Screen.FromPoint(new Point(cursorX, barTop - 1)).WorkingArea;
        int x = Math.Max(wa.Left + 8, Math.Min(wa.Right - w - 8, cursorX - w / 2));
        Bounds = new Rectangle(x, barTop - h - (int)(10 * u), w, h);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Card);
        float pad = 12 * u, y = pad, w = ClientSize.Width - pad * 2;
        using (var sub = new SolidBrush(Theme.Sub)) using (var text = new SolidBrush(Theme.Text)) using (var ac = new SolidBrush(accent))
        {
            g.DrawString(title, fTitle, sub, new RectangleF(pad, y, w, 20 * u), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            y += 22 * u;
            g.DrawString(value, fValue, text, pad, y - 2 * u);
            y += 30 * u;
            if (hist.Length > 1)
            {
                var r = new RectangleF(pad, y, w, 56 * u);
                double lo = hist.Min(), hi = hist.Max();
                if (hi - lo < 1e-9) { hi = lo + 1; }
                double floor = lo >= 0 && lo < hi * 0.5 ? 0 : lo; // start at 0 unless the values sit in a narrow high band
                var pts = new PointF[hist.Length];
                for (int i = 0; i < hist.Length; i++)
                    pts[i] = new PointF(r.X + r.Width * i / (hist.Length - 1), r.Bottom - (float)((hist[i] - floor) / (hi - floor)) * r.Height);
                using (var path = new GraphicsPath())
                {
                    path.AddLines(pts); path.AddLine(pts[pts.Length - 1], new PointF(r.Right, r.Bottom)); path.AddLine(new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom));
                    using (var fill = new LinearGradientBrush(r, Color.FromArgb(110, accent), Color.FromArgb(8, accent), 90f)) g.FillPath(fill, path);
                }
                using (var p = new Pen(accent, Math.Max(1.4f, 1.6f * u)) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
                using (var p = new Pen(Theme.Border, 1)) g.DrawLine(p, r.X, r.Bottom, r.Right, r.Bottom);
                y += 62 * u;
                g.DrawString(range, fSmall, sub, pad, y);
                y += 18 * u;
            }
            foreach (var l in lines)
            {
                g.DrawString(l, fSmall, l.StartsWith("•") ? text : sub, new RectangleF(pad, y, w, 18 * u), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                y += 18 * u;
            }
        }
        using (var p = new Pen(Theme.Border, 1)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
}

// A copy of the bar on another monitor's taskbar.
class MirrorBar : Form
{
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    readonly StatsBar bar;
    readonly DibSurface surface = new DibSurface();
    public StatsBar.BarLayout Last; public int H, X, Y;

    public MirrorBar(StatsBar bar) { this.bar = bar; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000; return cp; } // LAYERED | TOOLWINDOW | TOPMOST | NOACTIVATE
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    public void Push(StatsBar.BarLayout L, int height, int x, int y)
    {
        Last = L; H = height; X = x; Y = y;
        var bmp = surface.Canvas(L.Width, height);
        using (var g = Graphics.FromImage(bmp)) bar.PaintBar(g, L, height);
        surface.Push(Handle, x, y);
        ShowWindow(Handle, 8); // SW_SHOWNA
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
    }

    public void HideNow() { if (IsHandleCreated) ShowWindow(Handle, 0); Last = null; }

    protected override void OnMouseUp(MouseEventArgs e) { bar.Clicked(e.Button); base.OnMouseUp(e); }
    protected override void Dispose(bool disposing) { surface.Dispose(); base.Dispose(disposing); }
}

// ======================================================================= Wi-Fi signal
// Signal quality of the connected Wi-Fi network, straight from Windows' Native Wifi API (no netsh, no polling a process).
static class WifiSignal
{
    [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(int ver, IntPtr res, out int negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr handle, IntPtr res);
    [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr res, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr handle, ref Guid iface, int opcode, IntPtr res, out int size, out IntPtr data, IntPtr type);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

    static IntPtr handle;
    static readonly object gate = new object();

    // Returns 0–100, or -1 when there's no connected Wi-Fi adapter.
    public static int Read(out string ssid)
    {
        ssid = "";
        lock (gate)
        {
            try
            {
                int ver;
                if (handle == IntPtr.Zero && WlanOpenHandle(2, IntPtr.Zero, out ver, out handle) != 0) { handle = IntPtr.Zero; return -1; }
                IntPtr list;
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0) return -1;
                try
                {
                    int n = Marshal.ReadInt32(list);
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr item = list + 8 + i * 532; // WLAN_INTERFACE_INFO: GUID, WCHAR[256] description, state
                        if (Marshal.ReadInt32(item, 528) != 1) continue; // wlan_interface_state_connected
                        var guid = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                        int size; IntPtr data;
                        if (WlanQueryInterface(handle, ref guid, 7, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0) continue; // current connection
                        try
                        {
                            // WLAN_CONNECTION_ATTRIBUTES: state, mode, WCHAR[256] profile, then the association attributes.
                            int len = Math.Min(32, Marshal.ReadInt32(data, 520));
                            var raw = new byte[len];
                            Marshal.Copy(data + 524, raw, 0, len);
                            ssid = Encoding.UTF8.GetString(raw);
                            return Math.Max(0, Math.Min(100, Marshal.ReadInt32(data, 576)));
                        }
                        finally { WlanFreeMemory(data); }
                    }
                    return -1;
                }
                finally { WlanFreeMemory(list); }
            }
            catch { return -1; }
        }
    }

    public static void Close()
    {
        lock (gate) { if (handle != IntPtr.Zero) { WlanCloseHandle(handle, IntPtr.Zero); handle = IntPtr.Zero; } }
    }
}

// ======================================================================= Updates
// Asks GitHub for the latest release's version number. Nothing is downloaded or run automatically: a newer version
// opens the release page in the browser, where the user downloads it themselves.
static class Updates
{
    const string Repo = "https://api.github.com/repos/WastedDesigner/Kinetik/releases";
    const string Page = "https://github.com/WastedDesigner/Kinetik/releases/latest";
    const string Downloads = "https://github.com/WastedDesigner/Kinetik/releases/download/";

    public class Release
    {
        public Version Version; public string Suffix = "", Tag = "", Url = "", ZipUrl, ExeSha;
        public string Name { get { return Version.ToString(3) + (Suffix != "" ? "-" + Suffix : ""); } }
    }

    // This build's version, including any test suffix (e.g. 2.1.0-beta.1).
    public static string Current
    {
        get
        {
            var a = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
            return a != null ? a.InformationalVersion : Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        }
    }

    static readonly Regex TagRx = new Regex(@"^v?(\d+(?:\.\d+){1,3})(?:-([0-9A-Za-z.]+))?$");

    static bool Parse(string tag, out Version v, out string suffix)
    {
        v = null; suffix = "";
        var m = TagRx.Match(tag ?? "");
        if (!m.Success) return false;
        v = new Version(m.Groups[1].Value); suffix = m.Groups[2].Value;
        return true;
    }

    // Positive when a is newer than b. A test version comes before the release of the same number.
    static int Compare(Version av, string asuf, Version bv, string bsuf)
    {
        int c = av.CompareTo(bv);
        if (c != 0) return c;
        if (asuf == bsuf) return 0;
        if (asuf == "") return 1;
        if (bsuf == "") return -1;
        Func<string, int> num = x => { var m = Regex.Match(x, @"(\d+)$"); return m.Success ? int.Parse(m.Groups[1].Value) : 0; };
        return num(asuf).CompareTo(num(bsuf));
    }

    public static bool IsNewer(Release r)
    {
        Version cv; string cs;
        if (!Parse(Current, out cv, out cs)) return false;
        return Compare(r.Version, r.Suffix, cv, cs) > 0;
    }

    static string Get(string url, int limit, string accept)
    {
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.UserAgent = "Kinetik/" + Current;
        req.Accept = accept;
        req.Timeout = req.ReadWriteTimeout = 15000;
        using (var resp = req.GetResponse())
        using (var st = resp.GetResponseStream())
        using (var ms = new MemoryStream())
        {
            var buf = new byte[8192]; int n;
            while ((n = st.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > limit) throw new IOException("Response too large"); }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    static byte[] GetBytes(string url, int limit)
    {
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.UserAgent = "Kinetik/" + Current;
        req.Timeout = req.ReadWriteTimeout = 30000;
        using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
        {
            if (resp.ResponseUri.Scheme != "https") throw new IOException("Not a secure download");
            using (var st = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[65536]; int n;
                while ((n = st.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > limit) throw new IOException("Download too large"); }
                return ms.ToArray();
            }
        }
    }

    // done(newest release, error) runs on the UI thread of `owner`. Test versions only when beta is set.
    public static void Check(bool beta, Action<Release, string> done, Control owner)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Release best = null; string err = null;
            try
            {
                var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 4 << 20 };
                var body = Get(beta ? Repo + "?per_page=15" : Repo + "/latest", 4 << 20, "application/vnd.github+json");
                var list = beta ? ((object[])json.DeserializeObject(body)).Cast<Dictionary<string, object>>() : new[] { (Dictionary<string, object>)json.DeserializeObject(body) };
                foreach (var d in list)
                {
                    if (d.ContainsKey("draft") && true.Equals(d["draft"])) continue;
                    if (!beta && d.ContainsKey("prerelease") && true.Equals(d["prerelease"])) continue;
                    Version v; string suf;
                    if (!Parse(d["tag_name"] as string, out v, out suf)) continue;
                    var r = new Release { Version = v, Suffix = suf, Tag = (string)d["tag_name"], Url = d["html_url"] as string ?? Page };
                    var notes = d.ContainsKey("body") ? d["body"] as string ?? "" : "";
                    var m = Regex.Match(notes, @"SHA-256 of `Kinetik\.exe`:?\*?\*?\s*`([0-9A-Fa-f]{64})`");
                    if (m.Success) r.ExeSha = m.Groups[1].Value.ToUpperInvariant();
                    var assets = d.ContainsKey("assets") ? d["assets"] as object[] : null;
                    if (assets != null)
                        foreach (Dictionary<string, object> a in assets)
                        {
                            var url = a["browser_download_url"] as string;
                            if ((a["name"] as string) == "Kinetik.zip" && url != null && url.StartsWith(Downloads + r.Tag + "/")) r.ZipUrl = url;
                        }
                    if (best == null || Compare(r.Version, r.Suffix, best.Version, best.Suffix) > 0) best = r;
                }
                if (best == null) throw new FormatException("No releases found");
            }
            catch (Exception e) { err = e.Message; }
            try { owner.BeginInvoke((Action)(() => done(best, err))); } catch { }
        });
    }

    static readonly Regex LibEntry = new Regex(@"^lib/([A-Za-z0-9.\-_]+\.dll)$");

    // Downloads the release's zip into memory, checks Kinetik.exe against the SHA-256 in its release notes, and
    // replaces this copy's files in place. Files in use can't be overwritten, but they can be renamed, so the old
    // ones become *.old (deleted at the next start). New files are only ever created fresh, never written through
    // something already at that path. done(error) runs on the UI thread; on success Kinetik restarts.
    public static void Install(Release r, Action<string> done, Control owner)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string err = null;
            try
            {
                if (r.ZipUrl == null) throw new IOException("This release has no Kinetik.zip to install from.");
                if (r.ExeSha == null) throw new IOException("This release doesn't list a checksum for Kinetik.exe, so it can't be checked.");
                var zip = GetBytes(r.ZipUrl, 64 << 20);
                var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                using (var za = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read))
                    foreach (var en in za.Entries)
                    {
                        var name = en.FullName.Replace('\\', '/');
                        if (name.EndsWith("/")) continue;
                        if (name != "Kinetik.exe" && name != "LICENSE" && !LibEntry.IsMatch(name)) continue; // nothing else is written
                        if (en.Length > 32 << 20) throw new IOException(name + " is too large");
                        using (var st = en.Open()) using (var ms = new MemoryStream()) { st.CopyTo(ms); files[name] = ms.ToArray(); }
                    }
                byte[] exe;
                if (!files.TryGetValue("Kinetik.exe", out exe)) throw new IOException("The download doesn't contain Kinetik.exe.");
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(exe)).Replace("-", "") != r.ExeSha)
                        throw new IOException("The downloaded Kinetik.exe doesn't match the checksum in the release notes. Nothing was changed.");

                string dir = Path.GetDirectoryName(Application.ExecutablePath), me = Application.ExecutablePath;
                Directory.CreateDirectory(Path.Combine(dir, "lib"));
                var moved = new List<string>(); var written = new List<string>();
                try
                {
                    foreach (var f in files)
                    {
                        string target = f.Key == "Kinetik.exe" ? me : Path.Combine(dir, f.Key.Replace('/', '\\'));
                        if (File.Exists(target))
                        {
                            string old = target + ".old";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(target, old);
                            moved.Add(target);
                        }
                        using (var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write)) fs.Write(f.Value, 0, f.Value.Length);
                        written.Add(target);
                    }
                }
                catch
                {
                    foreach (var w in written) try { File.Delete(w); } catch { }
                    foreach (var m in moved) try { File.Move(m + ".old", m); } catch { }
                    throw;
                }
            }
            catch (UnauthorizedAccessException) { err = "Kinetik can't write to its folder. Run it as administrator, or update by hand."; }
            catch (Exception e) { err = e.Message; }
            try
            {
                owner.BeginInvoke((Action)(() =>
                {
                    if (err != null) { done(err); return; }
                    try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restart") { UseShellExecute = false }); } catch { }
                    Application.Exit();
                }));
            }
            catch { }
        });
    }

    // Removes the files an update left behind (see Install).
    public static void CleanUp()
    {
        try
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (var d in new[] { dir, Path.Combine(dir, "lib") })
                if (Directory.Exists(d))
                    foreach (var f in Directory.GetFiles(d, "*.old"))
                        try { File.Delete(f); } catch { }
        }
        catch { }
    }

    // Opened through Explorer so the browser starts as the normal user, even when Kinetik runs as administrator.
    public static void OpenReleasePage() { OpenUrl(Page); }

    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://github.com/WastedDesigner/Kinetik/")) url = Page;
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + url + "\"")); }
        catch { }
    }
}

// ======================================================================= Profiles and backups
// Settings as text: one "Name=value" line per setting, the same values as in the registry.
static class Profiles
{
    public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kinetik", "Profiles"); } }

    public static string Export(Settings c)
    {
        var sb = new StringBuilder("# Kinetik settings " + Updates.Current + "\r\n");
        foreach (var f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var v = f.GetValue(c);
            string text = v is bool ? ((bool)v ? "1" : "0") : v is int ? ((int)v).ToString(CultureInfo.InvariantCulture)
                        : v is float ? ((float)v).ToString(CultureInfo.InvariantCulture) : v is Color ? ((Color)v).ToArgb().ToString(CultureInfo.InvariantCulture)
                        : v is string ? Uri.EscapeDataString((string)v) : null;
            if (text != null) sb.Append(f.Name).Append('=').Append(text).Append("\r\n");
        }
        return sb.ToString();
    }

    // Applies the settings found in text and returns how many there were. Unknown or bad lines are skipped.
    public static int Import(Settings c, string text)
    {
        int n = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.StartsWith("#") || eq <= 0) continue;
            var f = Settings.Field(line.Substring(0, eq));
            if (f == null) continue;
            var v = line.Substring(eq + 1);
            try
            {
                if (f.FieldType == typeof(bool)) f.SetValue(c, v == "1");
                else if (f.FieldType == typeof(int)) f.SetValue(c, int.Parse(v, CultureInfo.InvariantCulture));
                else if (f.FieldType == typeof(float)) f.SetValue(c, float.Parse(v, CultureInfo.InvariantCulture));
                else if (f.FieldType == typeof(Color)) f.SetValue(c, Color.FromArgb(int.Parse(v, CultureInfo.InvariantCulture)));
                else if (f.FieldType == typeof(string)) f.SetValue(c, Uri.UnescapeDataString(v));
                else continue;
                n++;
            }
            catch { }
        }
        c.Sanitize();
        return n;
    }

    public static List<string> List()
    {
        try { return Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.kinetik").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToList() : new List<string>(); }
        catch { return new List<string>(); }
    }

    public static string Clean(string name)
    {
        name = new string((name ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_').ToArray()).Trim();
        return name.Length > 40 ? name.Substring(0, 40) : name;
    }

    public static void Save(Settings c, string name) { SafeFile.Write(Path.Combine(Dir, Clean(name) + ".kinetik"), Export(c)); }

    public static bool Load(Settings c, string name)
    {
        var path = Path.Combine(Dir, Clean(name) + ".kinetik");
        if (!File.Exists(path)) return false;
        Import(c, File.ReadAllText(path));
        c.Save();
        return true;
    }

    public static void Delete(string name) { try { File.Delete(Path.Combine(Dir, Clean(name) + ".kinetik")); } catch { } }
}

// ======================================================================= Tray icon artwork
// Kinetik's tray and taskbar icon: a three-bladed fan on a choice of backgrounds, drawn at the exact size needed.
// It spins with CPU load; the blades repeat every 120°, so a handful of cached frames makes a seamless loop.
static class TrayIconArt
{
    public const int Frames = 8;
    static readonly Dictionary<string, Icon> cache = new Dictionary<string, Icon>();

    // Name → top-left and bottom-right gradient colours; a transparent pair means no tile.
    public static readonly KeyValuePair<string, Color[]>[] Styles =
    {
        S("Kinetik orange (default)", 255, 255, 106, 43, 255, 255, 140, 60),
        S("Violet to cyan", 255, 108, 76, 255, 255, 0, 198, 255),
        S("Ocean blue", 255, 30, 90, 230, 255, 0, 170, 255),
        S("Emerald", 255, 0, 150, 90, 255, 60, 220, 140),
        S("Sunset", 255, 255, 80, 60, 255, 255, 170, 40),
        S("Crimson", 255, 170, 20, 50, 255, 255, 60, 90),
        S("Hot pink", 255, 200, 40, 160, 255, 255, 110, 200),
        S("Graphite", 255, 45, 45, 52, 255, 85, 85, 96),
        S("Midnight", 255, 20, 22, 40, 255, 40, 50, 90),
        S("Light", 255, 235, 235, 240, 255, 255, 255, 255),
        S("No background", 0, 0, 0, 0, 0, 0, 0, 0),
    };

    static KeyValuePair<string, Color[]> S(string n, int a1, int r1, int g1, int b1, int a2, int r2, int g2, int b2)
    {
        return new KeyValuePair<string, Color[]>(n, new[] { Color.FromArgb(a1, r1, g1, b1), Color.FromArgb(a2, r2, g2, b2) });
    }

    // Cached, so a spinning icon never allocates new icon handles.
    public static Icon Frame(int style, int size, int frame)
    {
        style = Math.Max(0, Math.Min(Styles.Length - 1, style));
        var key = style + "|" + size + "|" + frame;
        Icon ic;
        if (!cache.TryGetValue(key, out ic))
            using (var bmp = Draw(style, size, frame * Period / Frames))
                cache[key] = ic = Icon.FromHandle(bmp.GetHicon());
        return ic;
    }

    // The fan has three blades, so it looks the same every 120°.
    public const float Period = 120f;

    public static Bitmap Draw(int style, int size, float angle)
    {
        var st = Styles[style].Value;
        bool none = st[0].A == 0, light = st[0].GetBrightness() > 0.8f;
        int s = Math.Max(16, size);
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float m = Math.Max(0.5f, s * 0.03f), cx = s / 2f, cy = s / 2f;
            if (!none)
                using (var tile = Icons.Round(new RectangleF(m, m, s - 2 * m, s - 2 * m), s * 0.23f))
                using (var grad = new LinearGradientBrush(new RectangleF(0, 0, s, s), st[0], st[1], 45f))
                    g.FillPath(grad, tile);
            // White blades on colour; dark on the light tile; orange with no tile, so it shows on light and dark taskbars.
            Color blade = none ? Color.FromArgb(255, 106, 43) : light ? Color.FromArgb(40, 40, 50) : Color.White;
            float k = none ? 1.2f : 1f;
            using (var b = new SolidBrush(blade))
            {
                for (int q = 0; q < 3; q++)
                    using (var path = new GraphicsPath())
                    using (var mx = new Matrix())
                    {
                        path.AddEllipse(cx - s * 0.106f * k, cy - s * (0.1875f + 0.175f) * k, s * 0.212f * k, s * 0.35f * k);
                        mx.RotateAt(angle + q * 120, new PointF(cx, cy));
                        path.Transform(mx);
                        g.FillPath(b, path);
                    }
                // Hub: a ring in the tile colour around a small dot.
                float hr = s * 0.075f * k, dr = s * 0.037f * k;
                Color hub = none ? Color.White : Mix(st[0], st[1]);
                using (var hb = new SolidBrush(hub)) g.FillEllipse(hb, cx - hr, cy - hr, hr * 2, hr * 2);
                g.FillEllipse(b, cx - dr, cy - dr, dr * 2, dr * 2);
            }
        }
        return bmp;
    }

    static Color Mix(Color a, Color b) { return Color.FromArgb(255, (a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2); }
}

// ======================================================================= Taskbar button
// An invisible window whose only job is to put a Kinetik button on the taskbar. Clicking it opens Settings;
// "Close window" from its taskbar menu exits Kinetik, like any other app.
class TaskbarButtonForm : Form
{
    readonly StatsBar bar;
    bool closingApp;

    public TaskbarButtonForm(StatsBar bar)
    {
        this.bar = bar;
        Text = "Kinetik";
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
        Opacity = 0;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void WndProc(ref Message m)
    {
        // Clicking the button of the active window asks it to minimise; open Settings instead.
        if (m.Msg == 0x112 && ((int)m.WParam & 0xFFF0) == 0xF020) { bar.OpenSettings(null); return; }
        base.WndProc(ref m);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        BeginInvoke((Action)(() => bar.OpenSettings(null)));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !closingApp)
        {
            e.Cancel = true;
            closingApp = true;
            bar.BeginInvoke((Action)(() => bar.Close()));
            return;
        }
        base.OnFormClosing(e);
    }
}

// ======================================================================= Shortcuts
static class Shortcuts
{
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int ev, int flags, IntPtr a, IntPtr b);

    public static string DesktopPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Kinetik.lnk"); } }

    // A multi-size .ico of the fan logo in the chosen colour, for the shortcut (the exe's own icon is fixed at build time).
    static string IconFile(int style)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kinetik");
        Directory.CreateDirectory(dir);
        if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Icon folder is redirected");
        var path = Path.Combine(dir, "icon-" + style + ".v2.ico");
        if (File.Exists(path)) return path;
        // Classic 32-bit bitmap entries for the small sizes (Explorer won't reliably show PNG ones), PNG for 256.
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var data = sizes.Select(sz => { using (var b = TrayIconArt.Draw(style, sz, 0)) return sz >= 256 ? Png(b) : Dib(b); }).ToList();
        using (var w = new BinaryWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))) // fails rather than follow a planted file or link
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int k = 0; k < sizes.Length; k++)
            {
                byte dim = (byte)(sizes[k] >= 256 ? 0 : sizes[k]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32); w.Write(data[k].Length); w.Write(offset);
                offset += data[k].Length;
            }
            foreach (var e in data) w.Write(e);
        }
        return path;
    }

    static byte[] Png(Bitmap b) { using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }

    // An icon image in BMP form: header (double height for the mask), bottom-up BGRA pixels, then an all-clear AND mask.
    static byte[] Dib(Bitmap b)
    {
        int n = b.Width, maskRow = ((n + 31) / 32) * 4;
        using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(n); w.Write(n * 2); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(n * n * 4 + maskRow * n); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = n - 1; y >= 0; y--)
                for (int x = 0; x < n; x++) { var c = b.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
            w.Write(new byte[maskRow * n]);
            return ms.ToArray();
        }
    }

    public static void CreateDesktop(int style)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { DesktopPath });
            var t = lnk.GetType();
            Action<string, object> set = (name, v) => t.InvokeMember(name, BindingFlags.SetProperty, null, lnk, new[] { v });
            set("TargetPath", Application.ExecutablePath);
            set("WorkingDirectory", Path.GetDirectoryName(Application.ExecutablePath));
            set("Description", "Kinetik: live PC stats on your taskbar and desktop");
            string ico;
            try { ico = IconFile(style) + ",0"; } catch (Exception ex) { ico = Application.ExecutablePath + ",0"; Program.Log(ex); }
            set("IconLocation", ico);
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED: make Explorer redraw the icon
            Marshal.FinalReleaseComObject(lnk); Marshal.FinalReleaseComObject(shell);
        }
        catch { }
    }

    public static void RemoveDesktop() { try { File.Delete(DesktopPath); } catch { } }
}

// ======================================================================= Icon animation
// Brings icons to life on the bar and the widget. Each preset icon has its own animation that acts out
// what it shows (a fan spins, a thermometer's mercury rises, a gauge's needle swings, disk LEDs flicker…),
// driven by how busy its stat is. Each icon keeps its own phase, so a change of speed never makes it jump.
class IconAnim
{
    readonly Dictionary<string, double> phase = new Dictionary<string, double>();
    readonly Dictionary<string, double> shown = new Dictionary<string, double>(); // activity, eased per icon
    readonly Dictionary<string, string> kinds = new Dictionary<string, string>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    double last, dt, t;

    public void Advance()
    {
        double now = clock.Elapsed.TotalSeconds;
        dt = Math.Min(0.1, now - last); last = now; t = now;
        foreach (var k in phase.Keys.ToList())
        {
            double a, p = phase[k];
            shown.TryGetValue(k, out a);
            double speed = Speed(kinds[k], a);
            if (speed <= 0)
            {
                // An idle icon finishes its current cycle and comes to rest, rather than freezing mid-move.
                double f = p - Math.Floor(p);
                if (f < 0.001) continue;
                phase[k] = f + 0.8 * dt >= 1 ? Math.Ceiling(p) : p + 0.8 * dt;
                continue;
            }
            phase[k] = p + speed * dt;
        }
    }

    static readonly Dictionary<IconFn, string> iconKinds = new Dictionary<IconFn, string>
    {
        { Icons.Fan, "fan" }, { Icons.Gear, "gear" }, { Icons.Up, "up" }, { Icons.Down, "down" }, { Icons.UpDown, "updown" },
        { Icons.Chip, "chip" }, { Icons.Grid, "grid" }, { Icons.Gauge, "gauge" }, { Icons.Thermo, "thermo" }, { Icons.Flame, "flame" },
        { Icons.Bolt, "bolt" }, { Icons.Plug, "plug" }, { Icons.Ram, "ram" }, { Icons.Vram, "vram" }, { Icons.Gpu, "gpu" },
        { Icons.Disk, "disk" }, { Icons.DiskRead, "diskread" }, { Icons.DiskWrite, "diskwrite" }, { Icons.Signal, "signal" },
        { Icons.Wifi, "wifi" }, { Icons.Globe, "globe" }, { Icons.Pie, "pie" }, { Icons.Clock, "clock" }, { Icons.Hourglass, "hourglass" },
        { Icons.Pulse, "pulse" }, { Icons.Heart, "heart" }, { Icons.Monitor, "monitor" }, { Icons.List, "list" }, { Icons.Star, "star" },
        { Icons.Dot, "dot" }, { Icons.Layers, "shine" }, { Icons.Stack, "stack" },
    };

    static string Kind(string id, IconFn icon)
    {
        string k;
        if (icon != null && iconKinds.TryGetValue(icon, out k)) return k;
        if (id == null || id == "DiskFree") return "shine";
        if (id == "GpuFan") return "spinany";
        if (id.EndsWith("Temp")) return "heat";
        if (id.EndsWith("Power")) return "zap";
        if (id == "Ping") return "heart";
        return "shine";
    }

    // Cycles per second at a given activity level (0 = stand still).
    static double Speed(string kind, double a)
    {
        switch (kind)
        {
            case "fan": case "spinany": case "gpu": return a <= 0.01 ? 0 : 0.2 + a * 3.6;
            case "gear": return a <= 0.01 ? 0 : 0.08 + a * 0.9;
            case "up": case "down": case "updown": case "diskread": case "diskwrite": return a <= 0.02 ? 0 : 0.4 + a * 2;
            case "disk": return a <= 0.02 ? 0 : 0.5 + a * 2.5;
            case "clock": return 1;
            case "hourglass": return 0.18 + a * 0.35;
            case "heart": return 0.8 + a * 0.9;
            case "globe": return 0.12 + a * 0.6;
            default: return 0.3 + a * 1.5;
        }
    }

    static double Clamp(double v) { return double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v)); }
    static double Rate(double bytes) { return Clamp(Math.Log10(Math.Max(0, bytes) / 1024 + 1) / 4.5); } // 1 KB/s ≈ 0.07, 30 MB/s ≈ 1
    static double Noise(double x) { double v = Math.Sin(x * 12.9898 + 78.233) * 43758.5453; return v - Math.Floor(v); }
    static float Ease(double f) { f = Clamp(f); return (float)(f * f * (3 - 2 * f)); }

    public static double Activity(string id, Snapshot s)
    {
        switch (id)
        {
            case "Up": return Rate(s.Up);
            case "Down": return Rate(s.Down);
            case "NetTotal": return Rate(s.Up + s.Down);
            case "DiskRead": return Rate(s.DiskRead);
            case "DiskWrite": return Rate(s.DiskWrite);
            case "Disk": return Clamp(s.Disk / 100);
            case "Ping": return s.Ping >= 0 ? Clamp(1 - s.Ping / 250.0) : 0;
            case "Wifi": return s.Wifi >= 0 ? Clamp(s.Wifi / 100.0) : 0;
            case "Cpu": return Clamp(s.Cpu / 100);
            case "CpuCores": return s.Cores.Length > 0 ? Clamp(s.Cores.Average() / 100) : 0;
            case "CpuClock": return Clamp(s.CpuMhz / 5500);
            case "CpuTemp": return Clamp((s.CpuTemp - 30) / 65);
            case "CpuPower": return Clamp(s.CpuPower / 150);
            case "Ram": return Clamp(s.RamLoad / 100.0);
            case "RamGb": return s.RamTotal > 0 ? Clamp(s.RamUsed / s.RamTotal) : 0;
            case "RamCommit": return Clamp(s.Commit / 100);
            case "Gpu": return Clamp(s.GpuUtil / 100);
            case "GpuTemp": return Clamp((s.GpuTemp - 30) / 60);
            case "GpuClock": return Clamp(s.GpuMhz / 2800);
            case "GpuFan": return s.GpuFan < 0 ? 0 : Clamp(s.GpuFanRpm ? s.GpuFan / 3200 : s.GpuFan / 100);
            case "GpuVram": case "GpuVramPct": return s.VramTotal > 0 ? Clamp(s.VramUsed / s.VramTotal) : 0;
            case "GpuPower": return Clamp(s.GpuPower / 350);
            case "Fps": return double.IsNaN(s.Fps) ? 0 : Clamp(s.Fps / 144);
            case "BaseFps": return double.IsNaN(s.BaseFps) ? 0 : Clamp(s.BaseFps / 144);
            case "FpsLow": case "FpsLow01": return double.IsNaN(s.FpsLow) ? 0 : Clamp(s.FpsLow / 144);
            case "FrameTime": return double.IsNaN(s.FrameTime) ? 0 : Clamp(s.FrameTime / 33);
            case "Latency": return double.IsNaN(s.Latency) ? 0 : Clamp(s.Latency / 60);
            case "GpuHotspot": return Clamp((s.GpuHotspot - 30) / 70);
            case "GpuMemTemp": return Clamp((s.GpuMemTemp - 30) / 80);
            case "SsdTemp": return Clamp((s.SsdTemp - 25) / 50);
            case "RamTemp": return Clamp((s.RamTemp - 25) / 60);
            case "NetApp": return Rate(s.NetAppRate);
            case "Processes": return Clamp(s.Processes / 600.0);
            case "PcBattery": return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online ? 0.6 : 0.1;
            case "DiskFree": return 0.1;
            default: return 0.15;
        }
    }

    // The square redrawn each frame on the widget: the icon plus room for its glow and movement.
    public static RectangleF Cell(RectangleF r)
    {
        float m = r.Width * 0.15f;
        var c = r; c.Inflate(m, m);
        return RectangleF.FromLTRB((float)Math.Floor(c.Left), (float)Math.Floor(c.Top), (float)Math.Ceiling(c.Right), (float)Math.Ceiling(c.Bottom));
    }

    static Pen P(Color c, float s) { return new Pen(c, Math.Max(1.3f, s / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; }
    static Color A(Color c, double alpha) { return Color.FromArgb((int)(c.A * Clamp(alpha)), c); }

    static Color Mix(Color a, Color b, double f)
    {
        f = Clamp(f);
        return Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
    }

    static void Around(Graphics g, float cx, float cy, float angle, float scale)
    {
        g.TranslateTransform(cx, cy); g.RotateTransform(angle); g.ScaleTransform(scale, scale); g.TranslateTransform(-cx, -cy);
    }

    // A glint that sweeps across the icon: the icon redrawn brighter inside a slanted band.
    static void Shine(Graphics g, IconFn icon, RectangleF r, Color col, double pos, double strength)
    {
        if (strength <= 0.02) return;
        float s = r.Width, bx = r.X - s * 0.6f + (float)pos * s * 2.2f;
        for (int i = 0; i < 3; i++)
        {
            float w = s * (0.36f - i * 0.12f), x = bx + (s * 0.36f - w) / 2;
            using (var band = new GraphicsPath())
            {
                band.AddPolygon(new[] { new PointF(x + s * 0.3f, r.Y - 2), new PointF(x + s * 0.3f + w, r.Y - 2), new PointF(x + w - s * 0.3f, r.Bottom + 2), new PointF(x - s * 0.3f, r.Bottom + 2) });
                var st = g.Save();
                g.SetClip(band, CombineMode.Intersect);
                icon(g, r, Mix(col, Color.White, strength * (0.3 + 0.25 * i)));
                g.Restore(st);
            }
        }
    }

    public void Draw(Graphics g, string id, IconFn icon, RectangleF r, Color col, double act, bool warn)
    {
        string key = (id ?? "") + "|" + r.X.ToString("0") + "," + r.Y.ToString("0");
        string kind = Kind(id, icon);
        kinds[key] = kind;
        double a;
        if (!shown.TryGetValue(key, out a)) a = act;
        a += (act - a) * Math.Min(1, dt * 4); // ease towards the latest reading
        shown[key] = a;
        double p;
        if (!phase.TryGetValue(key, out p)) phase[key] = p = 0;

        float s = r.Width, cx = r.X + s / 2, cy = r.Y + s / 2;
        double frac = p - Math.Floor(p), wave = Math.Sin(p * 2 * Math.PI);
        Color hot = warn ? Color.OrangeRed : col;

        // Soft glow behind the icon that grows with activity.
        int glowA = (int)(a * 75 * (0.75 + 0.25 * wave));
        if (glowA > 3)
            using (var path = new GraphicsPath())
            {
                float gr = s * 0.65f;
                path.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(glowA, hot), SurroundColors = new[] { Color.FromArgb(0, hot) } })
                    g.FillPath(pb, path);
            }

        var state = g.Save();
        try { Paint(g, kind, id, icon, r, col, a, p, frac, wave, s, cx, cy); }
        finally { g.Restore(state); }
    }

    void Paint(Graphics g, string kind, string id, IconFn icon, RectangleF r, Color col, double a, double p, double frac, double wave, float s, float cx, float cy)
    {
        switch (kind)
        {
            case "fan":
            case "spinany":
            {
                // Spinning blades; at speed, fading ghost copies trail behind them like motion blur.
                double speed = Speed(kind, a);
                int ghosts = speed > 0.8 ? 3 : speed > 0.3 ? 1 : 0;
                for (int k = ghosts; k >= 1; k--)
                {
                    var st = g.Save();
                    Around(g, cx, cy, (float)(frac * 360 - k * speed * 7), 1);
                    icon(g, r, A(col, 0.28 / k));
                    g.Restore(st);
                }
                Around(g, cx, cy, (float)(frac * 360), 1);
                icon(g, r, col);
                break;
            }
            case "gear":
                Around(g, cx, cy, (float)(frac * 45), 1); // eight teeth, so 45° is one full step
                icon(g, r, col);
                Shine(g, icon, r, col, (p * 0.4) % 1.6 - 0.3, a * 0.6);
                break;

            case "up":
            case "down":
            {
                // The arrow streams in its direction, with speed streaks and a seamless following copy.
                bool down = kind == "down";
                float off = (float)(frac * s) * (down ? 1 : -1);
                var st = g.Save();
                g.SetClip(r, CombineMode.Intersect);
                g.TranslateTransform(0, off); icon(g, r, col);
                g.TranslateTransform(0, down ? -s : s); icon(g, r, col);
                g.Restore(st);
                Streaks(g, r, col, a, p, down);
                break;
            }
            case "updown":
            {
                // Left arrow flows up, right arrow flows down.
                float w = s * 0.18f, t0 = r.Y + s * 0.12f, b0 = r.Bottom - s * 0.12f, x1 = r.X + s * 0.3f, x2 = r.X + s * 0.7f, off = (float)(frac * s);
                using (var pen = P(col, s * 1.1f))
                {
                    var st = g.Save();
                    g.SetClip(r, CombineMode.Intersect);
                    for (int k = 0; k < 2; k++)
                    {
                        float o = -off + k * s;
                        g.DrawLine(pen, x1, t0 + o, x1, b0 + o); g.DrawLines(pen, new[] { new PointF(x1 - w, t0 + w + o), new PointF(x1, t0 + o), new PointF(x1 + w, t0 + w + o) });
                        o = off - k * s;
                        g.DrawLine(pen, x2, t0 + o, x2, b0 + o); g.DrawLines(pen, new[] { new PointF(x2 - w, b0 - w + o), new PointF(x2, b0 + o), new PointF(x2 + w, b0 - w + o) });
                    }
                    g.Restore(st);
                }
                break;
            }

            case "chip":
            {
                // Pins light up in a chase around the package; the die glows with load.
                icon(g, r, col);
                float i = s * 0.22f;
                var body = new RectangleF(r.X + i, r.Y + i, s - 2 * i, s - 2 * i);
                var pins = new List<PointF[]>();
                for (int k = 1; k <= 3; k++) pins.Add(new[] { new PointF(body.X + body.Width * k / 4f, r.Y + s * 0.06f), new PointF(body.X + body.Width * k / 4f, body.Y) });
                for (int k = 1; k <= 3; k++) pins.Add(new[] { new PointF(body.Right, body.Y + body.Height * k / 4f), new PointF(r.Right - s * 0.06f, body.Y + body.Height * k / 4f) });
                for (int k = 3; k >= 1; k--) pins.Add(new[] { new PointF(body.X + body.Width * k / 4f, body.Bottom), new PointF(body.X + body.Width * k / 4f, r.Bottom - s * 0.06f) });
                for (int k = 3; k >= 1; k--) pins.Add(new[] { new PointF(r.X + s * 0.06f, body.Y + body.Height * k / 4f), new PointF(body.X, body.Y + body.Height * k / 4f) });
                double head = frac * pins.Count;
                for (int k = 0; k < pins.Count; k++)
                {
                    double d = (head - k + pins.Count) % pins.Count; // how far behind the chase head this pin is
                    double lit = d < 4 ? (1 - d / 4) * (0.35 + 0.65 * a) : 0;
                    if (lit < 0.05) continue;
                    using (var pen = P(Mix(col, Color.White, lit * 0.8), s)) g.DrawLine(pen, pins[k][0], pins[k][1]);
                }
                using (var b = new SolidBrush(Mix(col, Color.White, a * (0.35 + 0.35 * wave))))
                {
                    float grow = (float)(a * 0.08 * (1 + wave)) * body.Width;
                    g.FillRectangle(b, body.X + body.Width * 0.3f - grow / 2, body.Y + body.Height * 0.3f - grow / 2, body.Width * 0.4f + grow, body.Height * 0.4f + grow);
                }
                break;
            }
            case "grid":
            {
                // Four cores, each filling and draining on its own like a tiny equaliser.
                float cell = s * 0.36f, gap = s * 0.12f, o = (s - cell * 2 - gap) / 2;
                for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        int n = i * 2 + j;
                        var cr = new RectangleF(r.X + o + i * (cell + gap), r.Y + o + j * (cell + gap), cell, cell);
                        double lvl = Clamp(0.15 + a * (0.55 + 0.45 * Math.Sin(t * (2.2 + n * 0.9 + a * 4) + n * 1.9)));
                        using (var path = Icons.Round(cr, s * 0.06f))
                        {
                            using (var b = new SolidBrush(A(col, 0.3))) g.FillPath(b, path);
                            var st = g.Save();
                            g.SetClip(new RectangleF(cr.X, cr.Bottom - cr.Height * (float)lvl, cr.Width, cr.Height * (float)lvl), CombineMode.Intersect);
                            using (var b = new SolidBrush(Mix(col, Color.White, lvl > 0.85 ? 0.3 : 0))) g.FillPath(b, path);
                            g.Restore(st);
                        }
                    }
                break;
            }
            case "gauge":
            {
                // The needle swings to the current load, with a little tremble when it's working hard.
                float gy = r.Y + s * 0.6f, rad = s * 0.42f;
                double v = Clamp(a + Math.Sin(t * 11) * 0.025 * a + Math.Sin(t * 1.3) * 0.02);
                float ang = (float)(160 + 220 * v);
                using (var dim = P(A(col, 0.35), s)) g.DrawArc(dim, cx - rad, gy - rad, rad * 2, rad * 2, 160, 220);
                using (var pen = P(col, s)) if (v > 0.01) g.DrawArc(pen, cx - rad, gy - rad, rad * 2, rad * 2, 160, 220 * (float)v);
                double ar = ang * Math.PI / 180;
                using (var pen = P(Mix(col, Color.White, 0.3), s)) g.DrawLine(pen, cx, gy, cx + (float)Math.Cos(ar) * rad * 0.78f, gy + (float)Math.Sin(ar) * rad * 0.78f);
                float d = s * 0.18f;
                using (var b = new SolidBrush(col)) g.FillEllipse(b, cx - d / 2, gy - d / 2, d, d);
                break;
            }
            case "thermo":
            {
                // Mercury rises with the temperature and turns hotter; heat waves shimmer off it when it runs hot.
                float br = s * 0.17f, by = r.Bottom - s * 0.22f;
                double lvl = Clamp(0.2 + a * 0.75 + Math.Sin(t * 2.6) * 0.03);
                Color merc = Mix(col, Color.OrangeRed, a * 0.7);
                using (var pen = P(col, s))
                using (var stem = Icons.Round(new RectangleF(cx - s * 0.1f, r.Y + s * 0.06f, s * 0.2f, s * 0.62f), s * 0.1f))
                    g.DrawPath(pen, stem);
                float top = by - (by - (r.Y + s * 0.14f)) * (float)lvl;
                using (var b = new SolidBrush(merc))
                {
                    g.FillEllipse(b, cx - br, by - br, br * 2, br * 2);
                    g.FillRectangle(b, cx - s * 0.045f, top, s * 0.09f, by - top);
                }
                if (a > 0.35)
                    using (var pen = new Pen(A(merc, (a - 0.35) * 1.5), Math.Max(1f, s / 14f)))
                        for (int k = 0; k < 2; k++)
                        {
                            double life = (t * (0.6 + a) + k * 0.5) % 1;
                            float x = cx + s * (k == 0 ? -0.3f : 0.3f), y = r.Bottom - (float)life * s;
                            pen.Color = A(merc, (a - 0.35) * 1.5 * Math.Sin(life * Math.PI));
                            var pts = new PointF[5];
                            for (int q = 0; q < 5; q++) pts[q] = new PointF(x + (float)Math.Sin(q * 1.6 + t * 6) * s * 0.05f, y - q * s * 0.07f);
                            g.DrawCurve(pen, pts);
                        }
                break;
            }
            case "flame":
            case "heat":
            {
                // Uneven flicker that's stronger and quicker the hotter it runs, with sparks rising off the top.
                double fl = Math.Sin(t * (6 + a * 10)) * 0.5 + Math.Sin(t * (13.7 + a * 7)) * 0.3 + Math.Sin(t * 2.3) * 0.2;
                float sy = 1 + (float)(fl * 0.08 * (0.3 + a)), sx = 1 - (float)(fl * 0.035 * (0.3 + a));
                var st = g.Save();
                g.TranslateTransform(cx, r.Bottom); g.ScaleTransform(sx, sy); g.RotateTransform((float)(Math.Sin(t * 3.1) * 3 * a)); g.TranslateTransform(-cx, -r.Bottom);
                icon(g, r, Mix(col, Color.OrangeRed, a * 0.5 * (0.6 + 0.4 * fl)));
                g.Restore(st);
                int sparks = (int)(a * 5);
                for (int k = 0; k < sparks; k++)
                {
                    double life = (t * (0.7 + a * 0.8) + k / (double)Math.Max(1, sparks)) % 1;
                    double seed = Math.Floor(t * (0.7 + a * 0.8) + k / (double)Math.Max(1, sparks)) + k * 17;
                    float x = cx + (float)((Noise(seed) - 0.5) * s * 0.6 + Math.Sin(life * 6 + k) * s * 0.05), y = r.Y + s * 0.4f - (float)life * s * 0.6f;
                    float d = s * 0.09f * (float)(1 - life);
                    using (var b = new SolidBrush(A(Mix(Color.Orange, Color.Yellow, Noise(seed + 3)), 1 - life))) g.FillEllipse(b, x - d / 2, y - d / 2, d, d);
                }
                break;
            }
            case "bolt":
            case "zap":
            case "plug":
            {
                // Crackles: the icon flashes bright and jolts, and jagged little arcs jump off it, more often under load.
                double n = Math.Sin(t * 23.1) * Math.Sin(t * 7.3 + 1) * Math.Sin(t * (3 + a * 9));
                bool crack = n > 0.5 - a * 0.45;
                float sc = crack ? 1.07f : 1f;
                var st = g.Save();
                Around(g, cx, cy, 0, sc);
                if (crack) g.TranslateTransform((float)(Math.Sin(t * 91) * s * 0.03), 0);
                icon(g, r, crack ? Mix(col, Color.White, 0.5) : col);
                g.Restore(st);
                if (a > 0.08)
                {
                    double slot = Math.Floor(t * 14);
                    int arcs = (int)(1 + a * 3);
                    using (var pen = new Pen(A(Mix(col, Color.White, 0.6), 0.9), Math.Max(1f, s / 16f)) { LineJoin = LineJoin.Round })
                        for (int k = 0; k < arcs; k++)
                        {
                            if (Noise(slot * 3.1 + k * 7.7) > 0.25 + a * 0.6) continue;
                            double ang = Noise(slot + k * 13.3) * Math.PI * 2;
                            float ox = kind == "plug" ? cx : cx + (float)Math.Cos(ang) * s * 0.18f, oy = kind == "plug" ? r.Y + s * 0.08f : cy + (float)Math.Sin(ang) * s * 0.18f;
                            var pts = new PointF[4];
                            for (int q = 0; q < 4; q++)
                            {
                                float len = s * (0.1f + q * 0.12f);
                                double jag = ang + (Noise(slot * 5 + k * 3 + q) - 0.5) * 1.2;
                                pts[q] = new PointF(ox + (float)Math.Cos(jag) * len, oy + (float)Math.Sin(jag) * len);
                            }
                            g.DrawLines(pen, pts);
                        }
                }
                break;
            }

            case "ram":
            {
                // Memory chips blink like activity LEDs, running along the stick.
                icon(g, r, col);
                var body = new RectangleF(r.X + s * 0.05f, r.Y + s * 0.25f, s * 0.9f, s * 0.42f);
                for (int k = 0; k < 3; k++)
                {
                    double d = (frac * 3 - k + 3) % 3;
                    double lit = Clamp(1 - d / 1.5) * (0.3 + 0.7 * a);
                    if (lit < 0.05) continue;
                    using (var b = new SolidBrush(Mix(col, Color.White, lit * 0.8)))
                        g.FillRectangle(b, body.X + body.Width * (0.12f + k * 0.29f), body.Y + body.Height * 0.28f, body.Width * 0.18f, body.Height * 0.44f);
                }
                Shine(g, icon, r, col, (p * 0.35) % 1.6 - 0.3, 0.25 + a * 0.3);
                break;
            }
            case "vram":
            {
                // Memory cells twinkle at random, busier as more of it fills up.
                float d = s * 0.13f;
                var body = new RectangleF(r.X + s * 0.12f, r.Y + s * 0.12f, s * 0.76f, s * 0.76f);
                using (var pen = P(col, s)) using (var path = Icons.Round(body, s * 0.1f)) g.DrawPath(pen, path);
                double slot = Math.Floor(t * (2 + a * 8));
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        bool on = Noise(slot * 1.7 + i * 3 + j * 11) < 0.25 + a * 0.65;
                        using (var b = new SolidBrush(on ? Mix(col, Color.White, 0.2 + 0.4 * a) : A(col, 0.35)))
                            g.FillRectangle(b, body.X + body.Width * (0.17f + i * 0.27f), body.Y + body.Height * (0.17f + j * 0.27f), d, d);
                    }
                break;
            }
            case "gpu":
            {
                // The card's own fan spins with GPU load, with motion-blur ghosts at speed.
                icon(g, r, col);
                var body = new RectangleF(r.X + s * 0.14f, r.Y + s * 0.2f, s * 0.8f, s * 0.5f);
                float fr = body.Height * 0.3f, fx = body.X + body.Width * 0.62f, fy = body.Y + body.Height / 2;
                double speed = Speed(kind, a);
                for (int gh = speed > 0.8 ? 2 : 0; gh >= 0; gh--)
                    using (var pen = new Pen(A(col, gh == 0 ? 1 : 0.3 / gh), Math.Max(1f, s / 13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        for (int k = 0; k < 3; k++)
                        {
                            double ang = (frac * 360 - gh * speed * 9 + k * 120) * Math.PI / 180;
                            g.DrawLine(pen, fx, fy, fx + (float)Math.Cos(ang) * fr * 0.85f, fy + (float)Math.Sin(ang) * fr * 0.85f);
                        }
                break;
            }

            case "disk":
            {
                // The activity LED flickers with disk use, and a glint runs along the drive.
                icon(g, r, col);
                var body = new RectangleF(r.X + s * 0.08f, r.Y + s * 0.25f, s * 0.84f, s * 0.5f);
                Led(g, body.Right - body.Width * 0.22f, body.Y + body.Height / 2, s * 0.12f, col, a);
                Shine(g, icon, r, col, (p * 0.3) % 1.6 - 0.3, a * 0.5);
                break;
            }
            case "diskread":
            case "diskwrite":
            {
                // Drive with a blinking LED, while the arrow streams out of (read) or into (write) it.
                bool read = kind == "diskread";
                var body = new RectangleF(r.X + s * 0.08f, r.Y + s * 0.6f, s * 0.84f, s * 0.32f);
                using (var pen = P(col, s)) using (var path = Icons.Round(body, s * 0.08f)) g.DrawPath(pen, path);
                Led(g, body.Right - body.Width * 0.2f, body.Y + body.Height / 2, s * 0.1f, col, a);
                float t0 = r.Y + s * 0.04f, bt = r.Y + s * 0.46f, w = s * 0.18f, span = bt - t0 + s * 0.12f;
                var st = g.Save();
                g.SetClip(RectangleF.FromLTRB(r.X, r.Y - 1, r.Right, body.Y - s * 0.04f), CombineMode.Intersect);
                using (var pen = P(col, s))
                    for (int k = 0; k < 2; k++)
                    {
                        float o = (float)(frac * span) * (read ? -1 : 1) + (read ? k * span : -k * span);
                        g.DrawLine(pen, cx, t0 + o, cx, bt + o);
                        if (read) g.DrawLines(pen, new[] { new PointF(cx - w, t0 + w + o), new PointF(cx, t0 + o), new PointF(cx + w, t0 + w + o) });
                        else g.DrawLines(pen, new[] { new PointF(cx - w, bt - w + o), new PointF(cx, bt + o), new PointF(cx + w, bt - w + o) });
                    }
                g.Restore(st);
                break;
            }

            case "signal":
            {
                // Bars light up one after another, and more of them stay lit the better the signal.
                float w = s * 0.18f;
                double level = a * 4;
                for (int k = 0; k < 4; k++)
                {
                    float h = s * (0.25f + k * 0.2f);
                    double sweep = Clamp(1 - Math.Abs(frac * 5 - k - 0.5));
                    double lit = Math.Max(k < Math.Ceiling(level) ? 0.85 : 0.3, sweep);
                    using (var b = new SolidBrush(Mix(A(col, lit), Color.White, sweep * 0.35)))
                        g.FillRectangle(b, r.X + s * 0.06f + k * s * 0.24f, r.Bottom - s * 0.1f - h, w, h);
                }
                break;
            }
            case "wifi":
            {
                // Waves radiate outward from the dot. On the Wi-Fi stat, arcs past the signal strength stay dark.
                float wy = r.Bottom - s * 0.16f;
                for (int k = 1; k <= 3; k++)
                {
                    bool reach = id != "Wifi" || a * 3 >= k - 0.5;
                    double lit = reach ? 0.3 + 0.7 * Clamp(1 - Math.Abs(frac * 4 - k)) : 0.15;
                    using (var pen = P(Mix(A(col, lit), Color.White, (lit - 0.3) * 0.3), s))
                    {
                        float rad = s * 0.23f * k;
                        g.DrawArc(pen, cx - rad, wy - rad, rad * 2, rad * 2, 225, 90);
                    }
                }
                float d = s * 0.16f * (float)(1 + 0.25 * Clamp(1 - frac * 4));
                using (var b = new SolidBrush(col)) g.FillEllipse(b, cx - d / 2, wy - d / 2, d, d);
                break;
            }
            case "globe":
            {
                // A turning globe: meridians slide round, bright on the near side and faint on the far side.
                float rad = s * 0.42f;
                using (var pen = P(col, s))
                {
                    g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                    g.DrawLine(pen, cx - rad, cy, cx + rad, cy);
                }
                for (int m = 0; m < 3; m++)
                {
                    double th = (frac + m / 3.0) * Math.PI;
                    float hw = (float)Math.Abs(Math.Cos(th)) * rad;
                    bool near = Math.Sin(th) > 0;
                    if (hw < 0.6f) continue;
                    using (var pen = new Pen(A(col, near ? 1 : 0.3), Math.Max(1f, s / (near ? 9f : 14f))))
                        g.DrawArc(pen, cx - hw, cy - rad, hw * 2, rad * 2, Math.Cos(th) > 0 ? -90 : 90, 180);
                }
                break;
            }
            case "pie":
            {
                // The slice grows to the current level and rotates slowly.
                using (var pen = P(col, s)) g.DrawEllipse(pen, r.X + s * 0.08f, r.Y + s * 0.08f, s * 0.84f, s * 0.84f);
                float sweep = (float)(40 + 300 * a + 12 * wave);
                using (var b = new SolidBrush(col)) g.FillPie(b, r.X + s * 0.2f, r.Y + s * 0.2f, s * 0.6f, s * 0.6f, (float)(-90 + p * 36), sweep);
                break;
            }
            case "clock":
            {
                // A ticking second hand that snaps from mark to mark, with a slowly turning hour hand.
                using (var pen = P(col, s))
                {
                    g.DrawEllipse(pen, r.X + s * 0.08f, r.Y + s * 0.08f, s * 0.84f, s * 0.84f);
                    double h = (t / 20) * 2 * Math.PI - Math.PI / 2;
                    g.DrawLine(pen, cx, cy, cx + (float)Math.Cos(h) * s * 0.18f, cy + (float)Math.Sin(h) * s * 0.18f);
                }
                double step = Math.Floor(p) + Ease(frac * 5);
                double sa = step / 12 * 2 * Math.PI - Math.PI / 2;
                using (var pen = new Pen(Mix(col, Color.White, 0.35), Math.Max(1f, s / 13f)) { EndCap = LineCap.Round })
                    g.DrawLine(pen, cx, cy, cx + (float)Math.Cos(sa) * s * 0.32f, cy + (float)Math.Sin(sa) * s * 0.32f);
                float d = s * 0.12f;
                using (var b = new SolidBrush(col)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
                break;
            }
            case "hourglass":
            {
                // Sand drains from top to bottom, then the glass flips over.
                double drain = Clamp(frac / 0.82), flip = Clamp((frac - 0.82) / 0.18);
                Around(g, cx, cy, Ease(flip) * 180, 1);
                float t0 = r.Y + s * 0.1f, bt = r.Bottom - s * 0.1f, l = r.X + s * 0.24f, rr = r.Right - s * 0.24f;
                using (var pen = P(col, s)) using (var b = new SolidBrush(col))
                {
                    g.DrawLine(pen, l - s * 0.06f, t0, rr + s * 0.06f, t0);
                    g.DrawLine(pen, l - s * 0.06f, bt, rr + s * 0.06f, bt);
                    g.DrawLines(pen, new[] { new PointF(l, t0), new PointF(cx - s * 0.05f, cy), new PointF(l, bt) });
                    g.DrawLines(pen, new[] { new PointF(rr, t0), new PointF(cx + s * 0.05f, cy), new PointF(rr, bt) });
                    float topAmt = (float)(1 - drain), botAmt = (float)drain, half = cy - t0 - s * 0.06f;
                    if (topAmt > 0.02f)
                    {
                        float sh = half * topAmt, sw = (rr - l - s * 0.1f) / 2 * topAmt;
                        g.FillPolygon(b, new[] { new PointF(cx, cy - s * 0.04f), new PointF(cx - sw, cy - s * 0.04f - sh), new PointF(cx + sw, cy - s * 0.04f - sh) });
                    }
                    if (botAmt > 0.02f)
                    {
                        float sh = half * botAmt, sw = (rr - l - s * 0.16f) / 2;
                        g.FillPolygon(b, new[] { new PointF(cx, bt - sh), new PointF(cx - sw, bt - s * 0.02f), new PointF(cx + sw, bt - s * 0.02f) });
                    }
                    if (drain < 1 && flip == 0)
                        using (var thin = new Pen(col, Math.Max(1f, s / 18f)) { DashPattern = new[] { 1.5f, 1.5f }, DashOffset = (float)(-t * 12) })
                            g.DrawLine(thin, cx, cy, cx, bt - half * botAmt);
                }
                break;
            }
            case "pulse":
            {
                // A heart-monitor trace: a bright head sweeps across leaving a fading line behind it.
                var pts = new[] {
                    new PointF(r.X + s * 0.04f, cy), new PointF(r.X + s * 0.28f, cy), new PointF(r.X + s * 0.4f, r.Y + s * 0.18f),
                    new PointF(r.X + s * 0.56f, r.Y + s * 0.82f), new PointF(r.X + s * 0.68f, cy), new PointF(r.Right - s * 0.04f, cy) };
                using (var pen = P(A(col, 0.25), s)) g.DrawLines(pen, pts);
                float hx = r.X + (float)(frac * 1.3 - 0.1) * s;
                for (int k = 0; k < 4; k++)
                {
                    var st = g.Save();
                    g.SetClip(RectangleF.FromLTRB(hx - s * (0.5f - k * 0.12f), r.Y - 2, hx, r.Bottom + 2), CombineMode.Intersect);
                    using (var pen = P(A(col, 0.35 + k * 0.2), s)) g.DrawLines(pen, pts);
                    g.Restore(st);
                }
                break;
            }
            case "heart":
            {
                // Lub-dub: two quick thumps then a rest, each thump sending out a fading ring.
                double b1 = Math.Exp(-Math.Pow((frac - 0.1) / 0.05, 2)), b2 = 0.6 * Math.Exp(-Math.Pow((frac - 0.28) / 0.05, 2));
                float sc = 1 + (float)((b1 + b2) * 0.16 * (0.5 + a));
                double ring = Clamp((frac - 0.1) / 0.6);
                if (ring > 0 && ring < 1)
                    using (var pen = new Pen(A(col, (1 - ring) * 0.6), Math.Max(1f, s / 14f)))
                    {
                        float rr = s * (0.3f + (float)ring * 0.4f);
                        g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                    }
                Around(g, cx, cy, 0, sc);
                icon(g, r, Mix(col, Color.White, (b1 + b2) * 0.3));
                break;
            }
            case "monitor":
            {
                // A scanline rolls down the screen, which flickers faintly with activity.
                icon(g, r, col);
                var body = new RectangleF(r.X + s * 0.06f, r.Y + s * 0.12f, s * 0.88f, s * 0.56f);
                var inner = RectangleF.Inflate(body, -s * 0.08f, -s * 0.08f);
                using (var b = new SolidBrush(A(col, 0.12 + a * 0.2 * (0.7 + 0.3 * Math.Sin(t * 17))))) g.FillRectangle(b, inner);
                float ly = inner.Y + inner.Height * (float)frac;
                using (var pen = new Pen(A(Mix(col, Color.White, 0.5), 0.8), Math.Max(1f, s / 16f))) g.DrawLine(pen, inner.X, ly, inner.Right, ly);
                break;
            }
            case "list":
            {
                // Lines write themselves in one after another, then clear and start again.
                float d = s * 0.14f;
                using (var pen = P(col, s)) using (var b = new SolidBrush(col))
                    for (int k = 0; k < 3; k++)
                    {
                        float y = r.Y + s * (0.22f + k * 0.28f);
                        double grow = Clamp(frac * 4 - k), fade = frac > 0.9 ? (1 - frac) * 10 : 1;
                        b.Color = A(col, 0.35 + 0.65 * Clamp(grow * 3) * fade);
                        g.FillEllipse(b, r.X + s * 0.08f, y - d / 2, d, d);
                        float x0 = r.X + s * 0.36f, x1 = r.Right - s * 0.06f;
                        using (var dim = P(A(col, 0.25), s)) g.DrawLine(dim, x0, y, x1, y);
                        pen.Color = A(col, fade);
                        if (grow > 0.02) g.DrawLine(pen, x0, y, x0 + (x1 - x0) * Ease(grow), y);
                    }
                break;
            }
            case "star":
            {
                // Slowly turns and twinkles, throwing off little sparkles.
                float sc = 1 + (float)(0.06 * wave);
                var st = g.Save();
                Around(g, cx, cy, (float)(Math.Sin(p * Math.PI) * 12), sc);
                icon(g, r, Mix(col, Color.White, 0.25 * Clamp(wave)));
                g.Restore(st);
                for (int k = 0; k < 2; k++)
                {
                    double life = (frac * 2 + k * 0.5) % 1;
                    float sx = r.X + s * (k == 0 ? 0.1f : 0.9f), sy = r.Y + s * (k == 0 ? 0.15f : 0.8f), len = s * 0.12f * (float)Math.Sin(life * Math.PI);
                    using (var pen = new Pen(A(Mix(col, Color.White, 0.5), Math.Sin(life * Math.PI)), Math.Max(1f, s / 18f)))
                    {
                        g.DrawLine(pen, sx - len, sy, sx + len, sy);
                        g.DrawLine(pen, sx, sy - len, sx, sy + len);
                    }
                }
                break;
            }
            case "dot":
            {
                // A sonar ping: rings ripple out from the dot.
                for (int k = 0; k < 2; k++)
                {
                    double life = (frac + k * 0.5) % 1;
                    float rr = s * (0.22f + 0.28f * (float)life);
                    using (var pen = new Pen(A(col, (1 - life) * 0.7), Math.Max(1f, s / 14f))) g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                }
                Around(g, cx, cy, 0, 1 + (float)(0.1 * wave));
                icon(g, r, col);
                break;
            }
            case "stack":
            {
                // The filled layer steps down through the stack.
                int lit = (int)Math.Floor(frac * 3) % 3;
                double f = frac * 3 - Math.Floor(frac * 3);
                using (var pen = P(col, s))
                    for (int k = 0; k < 3; k++)
                        using (var path = Icons.Round(new RectangleF(r.X + s * 0.1f, r.Y + s * (0.12f + k * 0.28f), s * 0.8f, s * 0.2f), s * 0.06f))
                        {
                            double on = k == lit ? Math.Sin(f * Math.PI) : 0;
                            g.DrawPath(pen, path);
                            using (var b = new SolidBrush(A(col, 0.25 + 0.75 * Math.Max(on, k == 0 ? 0.6 : 0)))) g.FillPath(b, path);
                        }
                break;
            }
            default: // "shine": anything else breathes gently and catches a glint of light as it passes
            {
                var st = g.Save();
                Around(g, cx, cy, 0, 1 + (float)(wave * (0.02 + a * 0.05)));
                icon(g, r, col);
                g.Restore(st);
                Shine(g, icon, r, col, (p * 0.45) % 1.6 - 0.3, 0.3 + a * 0.4);
                break;
            }
        }
    }

    // A drive activity light: flickers on at random, more often the busier the disk.
    void Led(Graphics g, float x, float y, float d, Color col, double a)
    {
        bool on = a > 0.02 && Noise(Math.Floor(t * (6 + a * 20))) < 0.3 + 0.65 * a;
        if (on)
            using (var path = new GraphicsPath())
            {
                float gr = d * 1.8f;
                path.AddEllipse(x - gr, y - gr, gr * 2, gr * 2);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(160, Mix(col, Color.White, 0.5)), SurroundColors = new[] { Color.FromArgb(0, col) } })
                    g.FillPath(pb, path);
            }
        float dd = on ? d * 1.2f : d;
        using (var b = new SolidBrush(on ? Mix(col, Color.White, 0.7) : A(col, 0.5))) g.FillEllipse(b, x - dd / 2, y - dd / 2, dd, dd);
    }

    // Speed streaks beside a flowing arrow, moving faster than the arrow itself.
    void Streaks(Graphics g, RectangleF r, Color col, double a, double p, bool down)
    {
        if (a < 0.1) return;
        float s = r.Width;
        using (var pen = new Pen(A(col, 0.2 + a * 0.4), Math.Max(1f, s / 16f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            for (int k = 0; k < 2; k++)
            {
                double life = (p * 1.7 + k * 0.5) % 1;
                float x = r.X + s * (k == 0 ? 0.12f : 0.88f), len = s * (0.15f + 0.2f * (float)a);
                float y = down ? r.Y + (float)life * (s + len) - len : r.Bottom - (float)life * (s + len);
                var st = g.Save();
                g.SetClip(r, CombineMode.Intersect);
                g.DrawLine(pen, x, y, x, y + len);
                g.Restore(st);
            }
    }
}

// ======================================================================= Desktop widget
// A floating panel of stats with usage bars and history graphs, grouped by hardware. It's a per-pixel-alpha
// window like the bar, so it gets smooth rounded corners, a soft shadow and a see-through background.
class WidgetForm : Form
{
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    // Recent values of one stat, oldest first, for its graph.
    class Hist
    {
        public readonly float[] V;
        public int Count, Head;
        public Hist(int n) { V = new float[n]; }
        public void Push(float x) { V[Head] = x; Head = (Head + 1) % V.Length; if (Count < V.Length) Count++; }
        public float At(int i) { return V[(Head - Count + i + V.Length) % V.Length]; }
    }

    class Item
    {
        public int Kind; // 0 stat row, 1 group heading, 2 divider, 3 title, 4 per-core chart
        public string Id, Label, Value, Sub, IconLabel, HistKey;
        public IconFn Icon;
        public Color Color;
        public bool Warn, Dim;
        public double Frac = double.NaN, GraphValue = double.NaN, GraphMax, GraphFloor = 1;
        public Hist Hist;
        public double[] Cores;
        public double Act; // 0-1 activity level that drives the icon animation
    }

    readonly StatsBar bar;
    readonly Settings c;
    public readonly int Layer;
    readonly DibSurface surface = new DibSurface();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly Dictionary<string, Hist> history = new Dictionary<string, Hist>();
    readonly SolidBrush brush = new SolidBrush(Color.White);
    Snapshot lastSnap;
    int winW, winH, margin;
    readonly IconAnim anim = new IconAnim();
    readonly List<KeyValuePair<Item, RectangleF>> animCells = new List<KeyValuePair<Item, RectangleF>>();
    readonly System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer { Interval = 16 };
    Color cardFill;
    bool dragging, clickThrough;
    Point grab;
    float u = 1, fontU;
    Font fLabel, fValue, fHead, fSmall, fTitle;
    static readonly StringFormat Trim = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
    static readonly StringFormat Typo = MakeTypo();

    static StringFormat MakeTypo()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        return f;
    }

    public WidgetForm(StatsBar bar)
    {
        this.bar = bar; c = bar.Cfg; Layer = c.WidgetLayer;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        clickThrough = ClickThroughWanted;

        menu.Renderer = new DarkMenuRenderer();
        var lockItem = new ToolStripMenuItem("Lock position");
        lockItem.Click += (s, e) => { c.WidgetLocked = !c.WidgetLocked; c.Save(); bar.NotifySettings("Widget"); Render(true); };
        menu.Opening += (s, e) => lockItem.Checked = c.WidgetLocked;
        menu.Items.Add("Widget settings…", null, (s, e) => bar.OpenSettings("Widget"));
        menu.Items.Add(lockItem);
        menu.Items.Add("Hide widget", null, (s, e) => bar.SetWidget(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Kinetik settings…", null, (s, e) => bar.OpenSettings(null));
        animTimer.Tick += (s, e) => { animTimer.Interval = 1000 / Math.Max(10, c.AnimFps); AnimFrame(); };
        animTimer.Start();
    }

    // Redraws just the icon squares for the next animation frame, then re-pushes the window.
    void AnimFrame()
    {
        anim.Advance();
        if (!c.WidgetAnimate || animCells.Count == 0 || !IsHandleCreated || !Visible || dragging) return;
        var bmp = surface.Canvas(winW, winH);
        using (var g = Graphics.FromImage(bmp))
        using (var fill = new SolidBrush(cardFill))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            foreach (var cell in animCells)
            {
                var r = IconAnim.Cell(cell.Value);
                g.SetClip(r);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.FillRectangle(fill, r);
                g.CompositingMode = CompositingMode.SourceOver;
                var it = cell.Key;
                anim.Draw(g, it.Id, it.Icon, cell.Value, it.Color, it.Act, it.Warn);
            }
        }
        surface.Push(Handle, c.WidgetX, c.WidgetY);
    }

    bool ClickThroughWanted { get { return c.WidgetLocked && c.WidgetClickThrough; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; // LAYERED | TOOLWINDOW | NOACTIVATE
            if (Layer == 2) cp.ExStyle |= 0x8;          // TOPMOST
            if (clickThrough) cp.ExStyle |= 0x20;       // TRANSPARENT: clicks fall through to what's underneath
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE → MA_NOACTIVATE: clicking it never steals focus
        if (m.Msg == 0x46 && Layer == 0) // WM_WINDOWPOSCHANGING: "on the desktop" keeps it underneath every other window
        {
            var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
            if ((wp.flags & 0x4) == 0) { wp.hwndInsertAfter = new IntPtr(1); Marshal.StructureToPtr(wp, m.LParam, false); }
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        surface.Dispose();
        if (disposing) { animTimer.Dispose(); DisposeFonts(); brush.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }

    public void HideNow() { if (IsHandleCreated) ShowWindow(Handle, 0); }

    public bool IsOnScreen(Screen s)
    {
        return Screen.FromRectangle(new Rectangle(c.WidgetX + margin, c.WidgetY + margin, Math.Max(1, winW - margin * 2), Math.Max(1, winH - margin * 2))).DeviceName == s.DeviceName;
    }

    // ---- dragging, snapping and the menu
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !c.WidgetLocked)
        {
            dragging = true;
            grab = new Point(Cursor.Position.X - c.WidgetX, Cursor.Position.Y - c.WidgetY);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = c.WidgetLocked ? Cursors.Default : Cursors.SizeAll;
        if (dragging)
        {
            var p = Snap(new Point(Cursor.Position.X - grab.X, Cursor.Position.Y - grab.Y));
            c.WidgetX = p.X; c.WidgetY = p.Y;
            SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, 0x1 | 0x4 | 0x10); // NOSIZE | NOZORDER | NOACTIVATE
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging) { dragging = false; c.Save(); }
        else if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        base.OnMouseUp(e);
    }

    // Pulls the card flush to a screen edge (with a small gap) when it's dragged close to one.
    Point Snap(Point p)
    {
        if (!c.WidgetSnap) return p;
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int gap = (int)(12 * u), reach = (int)(18 * u);
        int left = p.X + margin, top = p.Y + margin, right = p.X + winW - margin, bottom = p.Y + winH - margin;
        if (Math.Abs(left - (wa.Left + gap)) < reach) p.X = wa.Left + gap - margin;
        else if (Math.Abs(right - (wa.Right - gap)) < reach) p.X = wa.Right - gap - winW + margin;
        if (Math.Abs(top - (wa.Top + gap)) < reach) p.Y = wa.Top + gap - margin;
        else if (Math.Abs(bottom - (wa.Bottom - gap)) < reach) p.Y = wa.Bottom - gap - winH + margin;
        return p;
    }

    // A new or off-screen widget (e.g. its monitor was unplugged) goes to the top-right of the main screen.
    void EnsurePosition()
    {
        if (c.WidgetX == int.MinValue || !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(c.WidgetX, c.WidgetY, winW, winH))))
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int gap = (int)(12 * u);
            c.WidgetX = wa.Right - gap - winW + margin;
            c.WidgetY = wa.Top + gap - margin;
        }
    }

    // ---- fonts and theme
    void EnsureFonts()
    {
        if (fLabel != null && Math.Abs(fontU - u) < 0.001f) return;
        DisposeFonts();
        var fam = Theme.Family;
        fLabel = new Font(fam, 12.5f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        fValue = new Font(fam, 12.5f * u, FontStyle.Bold, GraphicsUnit.Pixel);
        fHead = new Font(fam, 10f * u, FontStyle.Bold, GraphicsUnit.Pixel);
        fSmall = new Font(fam, 10.5f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        fTitle = new Font(fam, 15f * u, FontStyle.Bold, GraphicsUnit.Pixel);
        fontU = u;
    }

    void DisposeFonts()
    {
        foreach (var f in new[] { fLabel, fValue, fHead, fSmall, fTitle }) if (f != null) f.Dispose();
        fLabel = fValue = fHead = fSmall = fTitle = null;
    }

    static bool appsLight; static DateTime appsLightAt;
    bool IsLight()
    {
        if (c.WidgetTheme != 2) return c.WidgetTheme == 1;
        if ((DateTime.UtcNow - appsLightAt).TotalSeconds > 3)
        {
            appsLightAt = DateTime.UtcNow;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    appsLight = k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1;
            }
            catch { appsLight = false; }
        }
        return appsLight;
    }

    // ---- what to show
    static string Group(string id)
    {
        if (id == "Up" || id == "Down" || id == "NetTotal" || id == "NetApp" || id == "Ping" || id == "Wifi" || id == "PublicIp" || id == "Vpn") return "Network";
        if (id.StartsWith("Cpu")) return "Processor";
        if (id.StartsWith("Ram")) return "Memory";
        if (id.StartsWith("Gpu") || id.StartsWith("Fps") || id == "BaseFps" || id == "FrameTime" || id == "Latency") return "Graphics";
        if (id.StartsWith("Disk") || id == "SsdTemp") return "Storage";
        if (id == "Processes" || id == "Uptime" || id == "Clock" || id == "Date") return "System";
        return "Battery";
    }

    static string CleanCpu(string n)
    {
        n = n.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "").Replace("Processor", "");
        int at = n.IndexOf('@');
        if (at > 0) n = n.Substring(0, at);
        n = Regex.Replace(n, @"\s\d+-Core", "");
        return Regex.Replace(n, @"\s+", " ").Trim();
    }

    static string HwName(string group, Snapshot s)
    {
        if (group == "Processor") return s.CpuName == "" ? null : CleanCpu(s.CpuName);
        if (group == "Graphics") return s.HasGpu ? s.GpuName : null;
        if (group == "Memory") return s.RamTotal > 0 ? s.RamTotal.ToString("0") + " GB" : null;
        return null;
    }

    static Item Graph(Item r, double value, double max, double floor)
    {
        r.HistKey = r.Id; r.GraphValue = value; r.GraphMax = max; r.GraphFloor = floor;
        return r;
    }

    // A percentage-style stat: bar, 0–100 graph, and the warning colour past its limit.
    static Item Pct(Item r, double value, int warnAt)
    {
        r.Frac = Math.Max(0, Math.Min(1, value / 100));
        r.Warn = value >= warnAt;
        return Graph(r, value, 100, 1);
    }

    static void TempRow(Func<string, string, Item> row, string label, double t, int warnAt)
    {
        if (double.IsNaN(t)) { var r = row(label, "--°C"); r.Dim = true; }
        else Pct(row(label, t.ToString("0") + "°C"), t, warnAt);
    }

    void AddStat(List<Item> rows, string id, Snapshot s)
    {
        var info = Stats.Get(id, c);
        bool customIcon = c.IconOf(id) != "";
        Func<string, string, Item> row = (label, value) =>
        {
            var it = new Item { Id = id, Label = label, Value = value, Icon = info.Icon, IconLabel = info.TextLabel ? info.Short : null, Color = info.Color };
            rows.Add(it);
            return it;
        };
        Item r;
        switch (id)
        {
            case "Up": Graph(row("Upload", StatsBar.Speed(s.Up)), s.Up, 0, 10240); break;
            case "Down": Graph(row("Download", StatsBar.Speed(s.Down)), s.Down, 0, 10240); break;
            case "NetTotal": Graph(row("Network total", StatsBar.Speed(s.Up + s.Down)), s.Up + s.Down, 0, 10240); break;
            case "Ping":
                if (s.Ping >= 0) { r = Graph(row("Ping", s.Ping + " ms"), s.Ping, 0, 20); r.Warn = s.Ping >= c.WarnPing; }
                else { r = row("Ping", s.Ping == -2 ? "timeout" : "-- ms"); r.Warn = s.Ping == -2; r.Dim = s.Ping == -1; }
                break;
            case "Wifi":
                if (s.Wifi >= 0) { r = Graph(row(s.WifiName != "" ? "Wi-Fi (" + s.WifiName + ")" : "Wi-Fi signal", s.Wifi + "%"), s.Wifi, 100, 1); r.Frac = s.Wifi / 100.0; r.Warn = s.Wifi <= 25; }
                break;
            case "Cpu": Pct(row("CPU usage", s.Cpu.ToString("0") + "%"), s.Cpu, c.WarnCpu); break;
            case "CpuCores":
                if (s.Cores.Length > 0) { r = row("CPU cores", "max " + s.Cores.Max().ToString("0") + "%"); r.Kind = 4; r.Cores = s.Cores; }
                break;
            case "CpuClock": if (s.CpuMhz > 0) Graph(row("CPU clock", (s.CpuMhz / 1000).ToString("0.00") + " GHz"), s.CpuMhz, 0, 1000); break;
            case "CpuTemp":
                if (double.IsNaN(s.CpuTemp)) { r = row("CPU temperature", "--°C"); r.Dim = true; }
                else Pct(row("CPU temperature", s.CpuTemp.ToString("0") + "°C"), s.CpuTemp, c.WarnCpuTemp);
                break;
            case "CpuPower": if (!double.IsNaN(s.CpuPower)) Graph(row("CPU power", s.CpuPower.ToString("0") + " W"), s.CpuPower, 0, 10); break;
            case "Ram": Pct(row("Memory usage", s.RamLoad + "%"), s.RamLoad, c.WarnRam); break;
            case "RamGb":
                r = Graph(row("Memory used", s.RamUsed.ToString("0.0") + " / " + s.RamTotal.ToString("0") + " GB"), s.RamUsed, s.RamTotal, 1);
                if (s.RamTotal > 0) r.Frac = s.RamUsed / s.RamTotal;
                break;
            case "RamCommit": Pct(row("Committed memory", s.Commit.ToString("0") + "%"), s.Commit, c.WarnRam); break;
            case "Gpu": if (s.HasGpu) Pct(row("GPU usage", s.GpuUtil.ToString("0") + "%"), s.GpuUtil, c.WarnGpu); break;
            case "GpuTemp": if (s.HasGpu) Pct(row("GPU temperature", s.GpuTemp.ToString("0") + "°C"), s.GpuTemp, c.WarnGpuTemp); break;
            case "GpuClock": if (s.HasGpu && s.GpuMhz > 0) Graph(row("GPU clock", s.GpuMhz.ToString("0") + " MHz"), s.GpuMhz, 0, 300); break;
            case "GpuFan":
                if (s.HasGpu && s.GpuFan >= 0)
                {
                    if (s.GpuFanRpm) Graph(row("GPU fan", s.GpuFan.ToString("0") + " rpm"), s.GpuFan, 0, 500);
                    else Pct(row("GPU fan", s.GpuFan.ToString("0") + "%"), s.GpuFan, 101);
                }
                break;
            case "GpuVram":
                if (s.HasGpu && s.VramTotal > 0)
                {
                    r = Graph(row("VRAM used", s.VramUsed.ToString("0.0") + " / " + s.VramTotal.ToString("0") + " GB"), s.VramUsed, s.VramTotal, 1);
                    r.Frac = s.VramUsed / s.VramTotal;
                }
                break;
            case "GpuVramPct":
                if (s.HasGpu && s.VramTotal > 0) { double p = s.VramUsed * 100 / s.VramTotal; Pct(row("VRAM usage", p.ToString("0") + "%"), p, c.WarnRam); }
                break;
            case "GpuPower": if (s.HasGpu) Graph(row("GPU power", s.GpuPower.ToString("0") + " W"), s.GpuPower, 0, 10); break;
            case "Fps":
            {
                string label = s.FpsApp != "" ? "Frame rate (" + s.FpsApp + ")" : "Frame rate";
                if (double.IsNaN(s.Fps)) { r = row(label, "-- fps"); r.Dim = true; }
                else Graph(row(label, s.Fps.ToString("0") + " fps"), s.Fps, 0, 30);
                break;
            }
            case "BaseFps":
                if (double.IsNaN(s.BaseFps)) { r = row("Base frame rate", "-- fps"); r.Dim = true; }
                else Graph(row("Base frame rate", s.BaseFps.ToString("0") + " fps"), s.BaseFps, 0, 30);
                break;
            case "FpsLow": case "FpsLow01":
            {
                double low = id == "FpsLow" ? s.FpsLow : s.FpsLow01;
                string label = id == "FpsLow" ? "1% low" : "0.1% low";
                if (double.IsNaN(low)) { r = row(label, "-- fps"); r.Dim = true; }
                else Graph(row(label, low.ToString("0") + " fps"), low, 0, 30);
                break;
            }
            case "FrameTime":
                if (double.IsNaN(s.FrameTime)) { r = row("Frame time", "-- ms"); r.Dim = true; }
                else Graph(row("Frame time", s.FrameTime.ToString("0.0") + " ms"), s.FrameTime, 0, 5);
                break;
            case "Latency":
                if (double.IsNaN(s.Latency)) { r = row("Render latency", "-- ms"); r.Dim = true; }
                else Graph(row("Render latency", s.Latency.ToString("0") + " ms"), s.Latency, 0, 5);
                break;
            case "GpuHotspot": TempRow(row, "GPU hot spot", s.GpuHotspot, c.WarnGpuTemp + 15); break;
            case "GpuMemTemp": TempRow(row, "GPU memory", s.GpuMemTemp, 100); break;
            case "SsdTemp": TempRow(row, "Drive temperature", s.SsdTemp, 70); break;
            case "RamTemp": TempRow(row, "RAM temperature", s.RamTemp, 85); break;
            case "NetApp":
                if (s.NetAppRate < 0) { r = row("Top network app", "--"); r.Dim = true; }
                else Graph(row(s.NetAppName != "" ? "Top app (" + s.NetAppName + ")" : "Top network app", StatsBar.Speed(s.NetAppRate)), s.NetAppRate, 0, 10240);
                break;
            case "PublicIp": r = row("Public IP", s.PublicIp != "" ? s.PublicIp : "--"); r.Dim = s.PublicIp == ""; break;
            case "Vpn": if (s.Vpn != null) { r = row("VPN", s.Vpn != "" ? s.Vpn : "Off"); r.Dim = s.Vpn == ""; } break;
            case "Clock": row("Time", DateTime.Now.ToString("t")); break;
            case "Date": row("Date", DateTime.Now.ToString("ddd d MMM")); break;
            case "Disk": if (s.Disk >= 0) Pct(row("Disk activity", s.Disk.ToString("0") + "%"), s.Disk, 95); break;
            case "DiskRead": if (s.DiskRead >= 0) Graph(row("Disk read", StatsBar.Speed(s.DiskRead)), s.DiskRead, 0, 10240); break;
            case "DiskWrite": if (s.DiskWrite >= 0) Graph(row("Disk write", StatsBar.Speed(s.DiskWrite)), s.DiskWrite, 0, 10240); break;
            case "DiskFree":
                if (s.FreeGb >= 0)
                {
                    r = row("Free space (" + s.FreeName + ")", StatsBar.SizeGb(s.FreeGb) + " free");
                    r.Frac = Math.Max(0, Math.Min(1, 1 - s.FreePct / 100)); // the bar shows how full the drive is
                    r.Warn = s.FreePct <= c.WarnFreePct;
                }
                break;
            case "Processes": if (s.Processes >= 0) Graph(row("Processes", s.Processes.ToString()), s.Processes, 0, 10); break;
            case "Uptime": row("Uptime", StatsBar.Duration(s.Uptime)); break;
            case "PcBattery":
            {
                var ps = SystemInformation.PowerStatus;
                if (ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryLifePercent <= 1)
                {
                    int p = (int)Math.Round(ps.BatteryLifePercent * 100);
                    r = row("Battery", (ps.PowerLineStatus == PowerLineStatus.Online ? "Charging · " : "") + p + "%");
                    r.Frac = p / 100.0; r.Warn = p <= c.WarnBattery;
                    if (!customIcon) r.Icon = (g, rr, cc) => Icons.Battery(g, rr, cc, p);
                }
                break;
            }
            case "BatTime":
            {
                var ps = SystemInformation.PowerStatus;
                if (ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.PowerLineStatus != PowerLineStatus.Online && ps.BatteryLifeRemaining > 0)
                    row("Battery left", StatsBar.Duration(ps.BatteryLifeRemaining));
                break;
            }
            case "Batteries":
                foreach (var d in bar.Sampler.Devices)
                {
                    int p = d.Value;
                    r = row(d.Key, p + "%");
                    r.Frac = p / 100.0; r.Warn = p <= c.WarnBattery;
                    if (!customIcon) r.Icon = Icons.ForDevice(d.Key, p);
                }
                break;
        }
    }

    int HistoryLength() { return Math.Max(10, Math.Min(600, c.WidgetGraphSecs * 1000 / Math.Max(250, c.Interval))); }

    List<Item> Build(Snapshot s, bool fresh)
    {
        var items = new List<Item>();
        if (!string.IsNullOrEmpty(c.WidgetTitle)) items.Add(new Item { Kind = 3, Label = c.WidgetTitle });
        string group = null;
        bool pendingDivider = false;
        foreach (var id in c.WidgetOrderList())
        {
            if (!c.WidgetIsOn(id)) continue;
            if (Settings.IsDivider(id)) { if (items.Count > 0) pendingDivider = true; continue; }
            var rows = new List<Item>();
            AddStat(rows, id, s);
            if (rows.Count == 0) continue;
            foreach (var rw in rows) rw.Act = IconAnim.Activity(id, s);
            var g = Group(id);
            if (c.WidgetHeadings && g != group)
            {
                // A heading already separates groups, so a divider right before one is dropped.
                items.Add(new Item { Kind = 1, Label = g.ToUpperInvariant(), Sub = c.WidgetHwNames ? HwName(g, s) : null });
                pendingDivider = false;
            }
            group = g;
            if (pendingDivider) { items.Add(new Item { Kind = 2 }); pendingDivider = false; }
            items.AddRange(rows);
        }
        if (items.Count == 0 || items.All(x => x.Kind == 3))
            items.Add(new Item { Label = "Right-click → Widget settings to pick stats", Value = "", Icon = Icons.Gear, Color = Theme.Accent, Dim = true });

        int n = HistoryLength();
        foreach (var it in items)
        {
            if (it.HistKey == null) continue;
            Hist h;
            if (!history.TryGetValue(it.HistKey, out h) || h.V.Length != n) history[it.HistKey] = h = new Hist(n);
            if (fresh) h.Push((float)it.GraphValue);
            it.Hist = h;
        }
        return items;
    }

    // ---- layout and drawing
    bool ShowBars { get { return c.WidgetStyle == 0 || c.WidgetStyle == 2; } }
    bool ShowGraphs { get { return c.WidgetStyle == 1 || c.WidgetStyle == 2; } }

    float HeightOf(Item it, bool first)
    {
        switch (it.Kind)
        {
            case 3: return 30 * u;
            case 1: return (first ? 18 : 30) * u;
            case 2: return 13 * u;
            case 4: return (20 + (c.WidgetStyle == 3 ? 18 : 28) + 6) * u;
            default:
                float h = 20 * u;
                if (ShowBars && !double.IsNaN(it.Frac)) h += 9 * u;
                if (ShowGraphs && it.Hist != null) h += 31 * u;
                return h + 6 * u;
        }
    }

    public void Render(bool force)
    {
        var s = bar.Sampler.Snap;
        bool fresh = s != lastSnap;
        if (!fresh && !force) return;
        lastSnap = s;
        if (clickThrough != ClickThroughWanted && IsHandleCreated)
        {
            clickThrough = ClickThroughWanted;
            int ex = GetWindowLong(Handle, -20);
            SetWindowLong(Handle, -20, clickThrough ? ex | 0x20 : ex & ~0x20);
        }
        u = DeviceDpi / 96f * c.WidgetScale / 100f;
        EnsureFonts();
        var items = Build(s, fresh);

        float pad = 14 * u, contentH = 0;
        for (int i = 0; i < items.Count; i++) contentH += HeightOf(items[i], i == 0);
        margin = c.WidgetShadow && c.WidgetOpacity >= 25 ? (int)Math.Ceiling(14 * u) : 1;
        int cardW = Math.Max(120, (int)(c.WidgetWidth * u));
        winW = cardW + margin * 2;
        winH = (int)Math.Ceiling(contentH + pad * 2 - 6 * u) + margin * 2;
        EnsurePosition();

        var bmp = surface.Canvas(winW, winH);
        using (var g = Graphics.FromImage(bmp)) PaintWidget(g, items, cardW, pad);
        surface.Push(Handle, c.WidgetX, c.WidgetY);
        ShowWindow(Handle, 8); // SW_SHOWNA
        if (Layer == 0) SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);       // bottom of the pile
        else if (Layer == 2) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // stay on top
    }

    void PaintWidget(Graphics g, List<Item> items, int cardW, float pad)
    {
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        bool light = IsLight();
        Color bg = c.WidgetBg.A != 0 ? Color.FromArgb(255, c.WidgetBg) : light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(22, 22, 26);
        Color text = light ? Color.FromArgb(24, 24, 28) : Color.FromArgb(240, 240, 244);
        Color sub = light ? Color.FromArgb(96, 96, 106) : Color.FromArgb(150, 150, 162);
        Color track = light ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(34, 255, 255, 255);
        Color line = light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(28, 255, 255, 255);
        int alpha = (int)Math.Round(c.WidgetOpacity * 2.55);

        var card = new RectangleF(margin, margin, cardW, winH - margin * 2);
        float rad = Math.Min(card.Height / 2, 12 * u * c.WidgetRound / 100f);
        if (margin > 1) // soft shadow: stacked, slightly larger translucent shapes
            for (int k = 7; k >= 1; k--)
            {
                var r = card;
                r.Inflate(k * 1.6f * u, k * 1.6f * u);
                r.Offset(0, 2 * u);
                Theme.FillRound(g, r, rad + k * 1.6f * u, Color.FromArgb(Math.Max(1, 7 * alpha / 255), 0, 0, 0));
            }
        cardFill = Color.FromArgb(Math.Max(1, alpha), bg);
        animCells.Clear();
        Theme.FillRound(g, card, rad, Color.FromArgb(Math.Max(1, alpha), bg)); // alpha ≥ 1 so the whole card still catches clicks
        if (alpha > 0) Theme.StrokeRound(g, card, rad, line, 1);

        float x0 = card.X + pad, x1 = card.Right - pad, y = card.Y + pad;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            float h = HeightOf(it, i == 0);
            switch (it.Kind)
            {
                case 3:
                    brush.Color = text;
                    g.DrawString(it.Label, fTitle, brush, new RectangleF(x0, y, x1 - x0, 22 * u), Trim);
                    break;
                case 1:
                {
                    float ty = y + h - 16 * u;
                    var hsz = g.MeasureString(it.Label, fHead, PointF.Empty, Typo);
                    brush.Color = sub;
                    g.DrawString(it.Label, fHead, brush, x0, ty, Typo);
                    if (!string.IsNullOrEmpty(it.Sub))
                    {
                        brush.Color = Color.FromArgb(170, sub);
                        var sf = new StringFormat(Trim) { Alignment = StringAlignment.Far };
                        g.DrawString(it.Sub, fSmall, brush, new RectangleF(x0 + hsz.Width + 10 * u, ty - 2 * u, x1 - x0 - hsz.Width - 10 * u, hsz.Height + 4 * u), sf);
                        sf.Dispose();
                    }
                    break;
                }
                case 2:
                    using (var p = new Pen(line, 1)) g.DrawLine(p, x0, y + 6 * u, x1, y + 6 * u);
                    break;
                default:
                    DrawRow(g, it, x0, x1, y, text, sub, track, line);
                    break;
            }
            y += h;
        }
    }

    void DrawRow(Graphics g, Item it, float x0, float x1, float y, Color text, Color sub, Color track, Color line)
    {
        float lineH = 20 * u, isz = 15 * u, lx = x0, w = x1 - x0;
        Color accent = it.Warn ? c.WarnColor : it.Color;

        if (it.IconLabel != null)
        {
            var sz = g.MeasureString(it.IconLabel, fHead, PointF.Empty, Typo);
            brush.Color = it.Color;
            g.DrawString(it.IconLabel, fHead, brush, x0, y + (lineH - sz.Height) / 2, Typo);
            lx += Math.Max(isz, sz.Width) + 8 * u;
        }
        else if (it.Icon != null)
        {
            var ir = new RectangleF(x0, y + (lineH - isz) / 2, isz, isz);
            if (c.WidgetAnimate) { anim.Draw(g, it.Id, it.Icon, ir, it.Color, it.Act, it.Warn); animCells.Add(new KeyValuePair<Item, RectangleF>(it, ir)); }
            else it.Icon(g, ir, it.Color);
            lx += isz + 9 * u;
        }

        var vsz = g.MeasureString(it.Value ?? "", fValue, PointF.Empty, Typo);
        brush.Color = it.Warn ? c.WarnColor : it.Dim ? sub : text;
        g.DrawString(it.Value ?? "", fValue, brush, x1 - vsz.Width, y + (lineH - vsz.Height) / 2, Typo);
        brush.Color = Color.FromArgb(215, text);
        g.DrawString(it.Label, fLabel, brush, new RectangleF(lx, y, Math.Max(1, x1 - vsz.Width - 10 * u - lx), lineH), Trim);

        float yy = y + lineH;
        if (it.Kind == 4)
        {
            float ch = (c.WidgetStyle == 3 ? 16 : 24) * u, top = yy + 3 * u;
            int n = it.Cores.Length;
            float gap = n > 24 ? 1 : Math.Max(1, 2 * u), bw = Math.Max(1, (w - gap * (n - 1)) / n);
            for (int k = 0; k < n; k++)
            {
                float bx = x0 + k * (bw + gap), bh = ch * (float)Math.Max(0, Math.Min(1, it.Cores[k] / 100));
                Theme.FillRound(g, new RectangleF(bx, top, bw, ch), Math.Min(bw / 2, 1.5f * u), track);
                if (bh > 0.5f) Theme.FillRound(g, new RectangleF(bx, top + ch - bh, bw, bh), Math.Min(bw / 2, 1.5f * u), it.Cores[k] >= c.WarnCpu ? c.WarnColor : it.Color);
            }
            return;
        }
        if (ShowBars && !double.IsNaN(it.Frac))
        {
            var tr = new RectangleF(x0, yy + 3 * u, w, 5 * u);
            Theme.FillRound(g, tr, tr.Height / 2, track);
            float fw = (float)(w * Math.Max(0, Math.Min(1, it.Frac)));
            if (fw >= 1) Theme.FillRound(g, new RectangleF(x0, tr.Y, Math.Max(fw, tr.Height), tr.Height), tr.Height / 2, accent);
            yy += 9 * u;
        }
        if (ShowGraphs && it.Hist != null) DrawGraph(g, it, new RectangleF(x0, yy + 5 * u, w, 24 * u), accent, line);
    }

    void DrawGraph(Graphics g, Item it, RectangleF r, Color col, Color line)
    {
        var h = it.Hist;
        int cap = h.V.Length, n = h.Count;
        using (var p = new Pen(line, 1)) g.DrawLine(p, r.X, r.Bottom, r.Right, r.Bottom);
        if (n < 2) return;
        double max = it.GraphMax;
        if (max <= 0) { max = it.GraphFloor; for (int i = 0; i < n; i++) max = Math.Max(max, h.At(i) * 1.15); }
        // Newest sample at the right edge; the graph fills in from the right as history builds up.
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float x = r.X + r.Width * (cap - n + i) / (cap - 1f);
            float v = (float)Math.Max(0, Math.Min(1, h.At(i) / max));
            pts[i] = new PointF(x, r.Bottom - v * r.Height);
        }
        using (var path = new GraphicsPath())
        {
            path.AddLines(pts);
            path.AddLine(pts[n - 1].X, pts[n - 1].Y, pts[n - 1].X, r.Bottom);
            path.AddLine(pts[n - 1].X, r.Bottom, pts[0].X, r.Bottom);
            path.CloseFigure();
            using (var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(110, col), Color.FromArgb(6, col), 90f))
                g.FillPath(fill, path);
        }
        using (var p = new Pen(col, Math.Max(1.2f, 1.5f * u)) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
    }
}

// ======================================================================= Game overlay
// A slim always-on-top strip of stats for gaming: one line of text over a see-through backdrop, in the corner of the screen.
// It shows over games running borderless or windowed. Exclusive fullscreen owns the whole display, so nothing can draw over it.
class OverlayForm : Form
{
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    readonly StatsBar bar;
    readonly Settings c;
    readonly DibSurface surface = new DibSurface();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly SolidBrush brush = new SolidBrush(Color.White);
    readonly IconAnim anim = new IconAnim();
    readonly System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer { Interval = 16 };
    readonly Bitmap measureBmp = new Bitmap(1, 1);
    readonly Graphics measureG;
    List<StatsBar.Seg> segs = new List<StatsBar.Seg>();
    string lastKey;
    int winW, winH;
    bool dragging, clickThrough;
    Point grab;
    Font font, bold; float fontPx, u = 1;
    float iconSz, iconGap, partGap, segGap, pad, barW, barGap;
    static readonly StringFormat Fmt = MakeFormat();

    static StringFormat MakeFormat()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    public OverlayForm(StatsBar bar)
    {
        this.bar = bar; c = bar.Cfg;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        clickThrough = c.OverlayLocked;
        measureG = Graphics.FromImage(measureBmp);

        menu.Renderer = new DarkMenuRenderer();
        var lockItem = new ToolStripMenuItem("Lock position (click-through)");
        lockItem.Click += (s, e) => { c.OverlayLocked = !c.OverlayLocked; c.Save(); bar.NotifySettings("Overlay"); Render(true); };
        menu.Opening += (s, e) => lockItem.Checked = c.OverlayLocked;
        menu.Items.Add("Overlay settings…", null, (s, e) => bar.OpenSettings("Overlay"));
        menu.Items.Add(lockItem);
        menu.Items.Add("Hide overlay", null, (s, e) => bar.SetOverlay(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Kinetik settings…", null, (s, e) => bar.OpenSettings(null));
        animTimer.Tick += (s, e) => { animTimer.Interval = 1000 / Math.Max(10, c.AnimFps); AnimFrame(); };
        animTimer.Start();
    }

    // Text labels don't move, so only icons need the per-frame repaint.
    bool Animated { get { return c.BarAnimate && !c.OverlayText; } }

    void AnimFrame()
    {
        anim.Advance();
        if (Animated && !dragging && IsHandleCreated && Visible && winW > 0) Draw();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000; // LAYERED | TOOLWINDOW | TOPMOST | NOACTIVATE
            if (clickThrough) cp.ExStyle |= 0x20;             // TRANSPARENT: clicks go to the game underneath
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE → MA_NOACTIVATE: never takes focus from the game
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        surface.Dispose();
        if (disposing)
        {
            animTimer.Dispose(); menu.Dispose(); brush.Dispose(); measureG.Dispose(); measureBmp.Dispose();
            if (font != null) font.Dispose();
            if (bold != null) bold.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---- dragging (while unlocked) and the menu
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { dragging = true; grab = new Point(Cursor.Position.X - c.OverlayX, Cursor.Position.Y - c.OverlayY); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = Cursors.SizeAll;
        if (dragging)
        {
            var p = Snap(new Point(Cursor.Position.X - grab.X, Cursor.Position.Y - grab.Y));
            c.OverlayX = p.X; c.OverlayY = p.Y;
            SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, 0x1 | 0x4 | 0x10); // NOSIZE | NOZORDER | NOACTIVATE
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging) { dragging = false; c.Save(); }
        if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        base.OnMouseUp(e);
    }

    // Games usually cover the whole screen, so it snaps to the screen's edges rather than the taskbar-free area.
    Point Snap(Point p)
    {
        var b = Screen.FromPoint(Cursor.Position).Bounds;
        int gap = (int)(8 * u), reach = (int)(16 * u);
        if (Math.Abs(p.X - (b.Left + gap)) < reach) p.X = b.Left + gap;
        else if (Math.Abs(p.X + winW - (b.Right - gap)) < reach) p.X = b.Right - gap - winW;
        else if (Math.Abs(p.X + winW / 2 - (b.Left + b.Width / 2)) < reach) p.X = b.Left + (b.Width - winW) / 2;
        if (Math.Abs(p.Y - (b.Top + gap)) < reach) p.Y = b.Top + gap;
        else if (Math.Abs(p.Y + winH - (b.Bottom - gap)) < reach) p.Y = b.Bottom - gap - winH;
        return p;
    }

    void EnsurePosition()
    {
        if (c.OverlayX == int.MinValue || !Screen.AllScreens.Any(sc => sc.Bounds.IntersectsWith(new Rectangle(c.OverlayX, c.OverlayY, winW, winH))))
        {
            var b = Screen.PrimaryScreen.Bounds;
            c.OverlayX = b.Left + (int)(8 * u);
            c.OverlayY = b.Top + (int)(8 * u);
        }
    }

    // ---- layout and drawing
    void EnsureFonts()
    {
        float px = (float)Math.Round(14 * u * 2) / 2;
        if (font != null && px == fontPx) return;
        if (font != null) font.Dispose();
        if (bold != null) bold.Dispose();
        font = new Font(c.FontName, px, FontStyle.Regular, GraphicsUnit.Pixel);
        bold = new Font(c.FontName, px, FontStyle.Bold, GraphicsUnit.Pixel);
        fontPx = px;
    }

    float Measure(string text, Font f) { return string.IsNullOrEmpty(text) ? 0 : measureG.MeasureString(text, f, PointF.Empty, Fmt).Width; }

    public void Render(bool force)
    {
        if (clickThrough != c.OverlayLocked && IsHandleCreated)
        {
            clickThrough = c.OverlayLocked;
            int ex = GetWindowLong(Handle, -20);
            SetWindowLong(Handle, -20, clickThrough ? ex | 0x20 : ex & ~0x20);
        }
        u = DeviceDpi / 96f * c.OverlayScale / 100f;
        EnsureFonts();
        Color val = Color.FromArgb(245, 245, 248), dim = Color.FromArgb(160, 160, 170);
        segs = bar.BuildSegments(bar.Sampler.Snap, val, dim, c.OverlayOrderList(), c.OverlayIsOn, c.OverlayText);
        if (segs.Count == 0)
            segs.Add(new StatsBar.Seg { Icon = Icons.Gear, IconColor = dim, Parts = { new StatsBar.Part { Text = "Right-click → Overlay settings", Template = "", Color = dim } } });

        iconSz = (float)Math.Round(fontPx * 1.1f); iconGap = fontPx * 0.4f; partGap = fontPx * 0.4f;
        segGap = fontPx * 1.0f; pad = fontPx * 0.7f;
        barW = Math.Max(2, fontPx * 0.22f); barGap = Math.Max(1, barW * 0.45f);
        float w = 0;
        foreach (var seg in segs)
        {
            if (seg.IsSep) { seg.W = 1; w += seg.W + segGap; continue; }
            float sw = 0;
            if (seg.Label != null) { seg.LabelW = Measure(seg.Label, bold); sw += seg.LabelW + iconGap; }
            else if (seg.Icon != null) sw += iconSz + iconGap;
            if (seg.Bars != null) sw += seg.Bars.Length * (barW + barGap) - barGap;
            for (int i = 0; i < seg.Parts.Count; i++)
            {
                var p = seg.Parts[i];
                p.W = Math.Max(Measure(p.Text, p.Bold ? bold : font), Measure(p.Template, p.Bold)); // templates stop the strip jittering
                sw += p.W + (i > 0 ? partGap : 0);
            }
            seg.W = (float)Math.Ceiling(sw);
            w += seg.W + segGap;
        }
        winW = Math.Max(8, (int)Math.Ceiling(w - segGap + pad * 2));
        winH = (int)Math.Ceiling(Math.Max(fontPx * 1.25f, iconSz) + fontPx * 0.7f);
        EnsurePosition();

        var k = new StringBuilder();
        k.Append(winW).Append('x').Append(winH).Append(c.OverlayOpacity).Append(c.OverlayX).Append(',').Append(c.OverlayY);
        foreach (var seg in segs)
        {
            k.Append('|').Append(seg.Label).Append(seg.IconColor.ToArgb());
            foreach (var p in seg.Parts) k.Append(';').Append(p.Text).Append(p.Color.ToArgb());
            if (seg.Bars != null) foreach (var b in seg.Bars) k.Append(',').Append((int)b);
        }
        var key = k.ToString();
        if (force || key != lastKey || Animated) Draw();
        lastKey = key;
        ShowWindow(Handle, 8); // SW_SHOWNA
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // back on top, in case the game went topmost
    }



    float Measure(string text, bool isBold) { return Measure(text, isBold ? bold : font); }

    void Draw()
    {
        var bmp = surface.Canvas(winW, winH);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int alpha = Math.Max(1, (int)Math.Round(c.OverlayOpacity * 2.55)); // ≥ 1 so the strip still catches clicks while unlocked
            Theme.FillRound(g, new RectangleF(0, 0, winW, winH), Math.Min(winH / 2f, 6 * u), Color.FromArgb(alpha, 10, 10, 14));

            float rowH = Math.Max(fontPx * 1.25f, iconSz), cy = (winH - rowH) / 2, textY = (winH - font.GetHeight(g)) / 2;
            float x = pad, sh = Math.Max(1, u);
            var shadow = Color.FromArgb(c.OverlayOpacity < 50 ? 170 : 90, 0, 0, 0); // keeps text readable over bright scenes
            Action<string, Font, Color, float> text = (t, f, col, tx) =>
            {
                brush.Color = shadow; g.DrawString(t, f, brush, tx + sh, textY + sh, Fmt);
                brush.Color = col; g.DrawString(t, f, brush, tx, textY, Fmt);
            };
            foreach (var seg in segs)
            {
                if (seg.IsSep)
                {
                    using (var p = new Pen(Color.FromArgb(90, seg.IconColor), Math.Max(1, u))) g.DrawLine(p, x, winH * 0.25f, x, winH * 0.75f);
                    x += seg.W + segGap;
                    continue;
                }
                float sx = x;
                if (seg.Label != null) { text(seg.Label, bold, seg.IconColor, sx); sx += seg.LabelW + iconGap; }
                else if (seg.Icon != null)
                {
                    var ir = new RectangleF(sx, (winH - iconSz) / 2, iconSz, iconSz);
                    if (c.BarAnimate) anim.Draw(g, seg.Id, seg.Icon, ir, seg.IconColor, seg.Act, seg.IconColor == c.WarnColor);
                    else seg.Icon(g, ir, seg.IconColor);
                    sx += iconSz + iconGap;
                }
                if (seg.Bars != null)
                {
                    float by = (winH - iconSz) / 2;
                    using (var bg = new SolidBrush(Color.FromArgb(60, seg.BarColor))) using (var fg = new SolidBrush(seg.BarColor))
                        for (int i = 0; i < seg.Bars.Length; i++)
                        {
                            float bx = sx + i * (barW + barGap), h = iconSz * (float)seg.Bars[i] / 100f;
                            g.FillRectangle(bg, bx, by, barW, iconSz);
                            g.FillRectangle(fg, bx, by + iconSz - h, barW, h);
                        }
                    sx += seg.Bars.Length * (barW + barGap) - barGap;
                }
                for (int i = 0; i < seg.Parts.Count; i++)
                {
                    var p = seg.Parts[i];
                    if (i > 0) sx += partGap;
                    text(p.Text, p.Bold ? bold : font, p.Color, sx);
                    sx += p.W;
                }
                x += seg.W + segGap;
            }
        }
        surface.Push(Handle, c.OverlayX, c.OverlayY);
    }
}

// ======================================================================= Settings UI
static class Theme
{
    public static Color Bg = Color.FromArgb(28, 28, 32), Nav = Color.FromArgb(22, 22, 26), Card = Color.FromArgb(38, 38, 44);
    public static Color CardHover = Color.FromArgb(46, 46, 53), Border = Color.FromArgb(56, 56, 64);

    // Background colour choices for the settings window: name → page background. Nav, cards and borders are derived from it.
    public static readonly KeyValuePair<string, Color>[] Backgrounds =
    {
        new KeyValuePair<string, Color>("Charcoal (default)", Color.FromArgb(28, 28, 32)),
        new KeyValuePair<string, Color>("Pure black", Color.FromArgb(8, 8, 10)),
        new KeyValuePair<string, Color>("Slate", Color.FromArgb(30, 36, 44)),
        new KeyValuePair<string, Color>("Midnight blue", Color.FromArgb(18, 24, 44)),
        new KeyValuePair<string, Color>("Deep violet", Color.FromArgb(32, 22, 50)),
        new KeyValuePair<string, Color>("Forest", Color.FromArgb(18, 34, 28)),
        new KeyValuePair<string, Color>("Wine", Color.FromArgb(42, 18, 26)),
        new KeyValuePair<string, Color>("Espresso", Color.FromArgb(38, 28, 22)),
    };

    public static void Use(int background)
    {
        var b = Backgrounds[Math.Max(0, Math.Min(Backgrounds.Length - 1, background))].Value;
        Func<int, Color> lift = k => Color.FromArgb(Math.Min(255, b.R + k), Math.Min(255, b.G + k), Math.Min(255, b.B + k));
        Bg = b;
        Nav = Color.FromArgb(Math.Max(0, b.R - 6), Math.Max(0, b.G - 6), Math.Max(0, b.B - 6));
        Card = lift(10); CardHover = lift(18); Border = lift(28);
    }
    public static readonly Color Text = Color.FromArgb(236, 236, 240), Sub = Color.FromArgb(150, 150, 162), Accent = Color.FromArgb(76, 194, 255);
    public static readonly Color WarnText = Color.FromArgb(255, 110, 110);
    static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    static string family;

    public static string Family
    {
        get
        {
            if (family == null) family = FontList.Has("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            return family;
        }
    }

    // Cached: callers must not dispose these.
    public static Font UI(float size, FontStyle st = FontStyle.Regular)
    {
        var key = size + "|" + st;
        Font f;
        if (!fonts.TryGetValue(key, out f)) fonts[key] = f = new Font(Family, size, st);
        return f;
    }

    public static void Chevron(Graphics g, RectangleF r, bool up, Color col)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, w = 5f, h = 3f;
        using (var p = new Pen(col, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            if (up) g.DrawLines(p, new[] { new PointF(cx - w, cy + h), new PointF(cx, cy - h), new PointF(cx + w, cy + h) });
            else g.DrawLines(p, new[] { new PointF(cx - w, cy - h), new PointF(cx, cy + h), new PointF(cx + w, cy - h) });
    }

    public static void Cross(Graphics g, RectangleF r, Color col)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, a = 4.5f;
        using (var p = new Pen(col, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        { g.DrawLine(p, cx - a, cy - a, cx + a, cy + a); g.DrawLine(p, cx - a, cy + a, cx + a, cy - a); }
    }

    public static void FillRound(Graphics g, RectangleF r, float rad, Color col)
    {
        using (var path = Icons.Round(r, rad)) using (var b = new SolidBrush(col)) g.FillPath(b, path);
    }

    public static void StrokeRound(Graphics g, RectangleF r, float rad, Color col, float width)
    {
        using (var path = Icons.Round(r, rad)) using (var p = new Pen(col, width)) g.DrawPath(p, path);
    }
}

class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Text; base.OnRenderItemText(e); }
    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemBorder { get { return Theme.CardHover; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
    }
}

class Toggle : Control
{
    bool on;
    public event EventHandler Changed;
    public bool On { get { return on; } set { on = value; Invalidate(); } }
    public Toggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(44, 22); Cursor = Cursors.Hand; BackColor = Color.Transparent;
    }
    protected override void OnClick(EventArgs e) { On = !On; if (Changed != null) Changed(this, e); base.OnClick(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Draw(e.Graphics, new RectangleF(1, 1, Width - 3, Height - 3), on, 255);
    }
    public static void Draw(Graphics g, RectangleF r, bool on, int alpha)
    {
        using (var path = Icons.Round(r, r.Height / 2))
        {
            if (on) using (var b = new SolidBrush(Color.FromArgb(alpha, Theme.Accent))) g.FillPath(b, path);
            else using (var p = new Pen(Color.FromArgb(alpha, Theme.Sub), 1.5f)) g.DrawPath(p, path);
        }
        float d = r.Height - (on ? 8 : 10), x = on ? r.Right - d - 4 : r.X + 5;
        using (var b = new SolidBrush(on ? Theme.Bg : Color.FromArgb(alpha, Theme.Sub))) g.FillEllipse(b, x, r.Y + (r.Height - d) / 2, d, d);
    }
}

class Swatch : Control
{
    Color color;
    public Color Value { get { return color; } set { color = value; Invalidate(); } }
    public Swatch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(44, 24); Cursor = Cursors.Hand; BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Icons.Round(new RectangleF(1, 1, Width - 3, Height - 3), 6))
        using (var b = new SolidBrush(color)) using (var p = new Pen(Theme.Border, 1.5f))
        { g.FillPath(b, path); g.DrawPath(p, path); }
    }
}

// A themed slider with its value shown on the right. Mouse, arrow keys, and the wheel (once focused) all work.
class Slider : Control
{
    readonly int min, max;
    int value; bool dragging;
    public string Unit = "%";
    public event EventHandler ValueChanged;

    public Slider(int min, int max, int value)
    {
        this.min = min; this.max = max; this.value = Math.Max(min, Math.Min(max, value));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        Size = new Size(230, 26); Cursor = Cursors.Hand; BackColor = Theme.Card; TabStop = true;
    }

    public int Value
    {
        get { return value; }
        set
        {
            int v = Math.Max(min, Math.Min(max, value));
            if (v == this.value) return;
            this.value = v; Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    Rectangle Track { get { return new Rectangle(9, Height / 2 - 2, Width - 76, 4); } }

    void SetFromX(int x)
    {
        var t = Track;
        Value = min + (int)Math.Round((double)(x - t.X) / t.Width * (max - min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) { dragging = true; SetFromX(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!Focused) { base.OnMouseWheel(e); return; } // let the page scroll unless the slider was clicked first
        Value += Math.Sign(e.Delta) * Math.Max(1, (max - min) / 50);
        var h = e as HandledMouseEventArgs;
        if (h != null) h.Handled = true;
    }
    protected override bool IsInputKey(Keys k) { var key = k & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Value--;
        else if (e.KeyCode == Keys.Right) Value++;
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        var t = Track;
        float x = t.X + (float)(value - min) / Math.Max(1, max - min) * t.Width;
        Theme.FillRound(g, t, 2, Theme.Border);
        Theme.FillRound(g, new RectangleF(t.X, t.Y, Math.Max(4, x - t.X), t.Height), 2, Theme.Accent);
        using (var b = new SolidBrush(Theme.Text)) g.FillEllipse(b, x - 7, Height / 2f - 7, 14, 14);
        if (Focused) using (var p = new Pen(Theme.Accent, 2)) g.DrawEllipse(p, x - 8, Height / 2f - 8, 16, 16);
        var f = Theme.UI(9f);
        var text = value + Unit;
        var sz = g.MeasureString(text, f);
        using (var b = new SolidBrush(Theme.Sub)) g.DrawString(text, f, b, Width - sz.Width - 2, (Height - sz.Height) / 2);
    }
}

// A settings row: icon, title, optional description, and a control on the right.
class Row : Panel
{
    readonly IconFn icon; readonly Color iconColor; readonly string title, desc;
    readonly Control right;
    static readonly StringFormat Clip = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
    public Row(IconFn icon, Color iconColor, string title, string desc, Control right, int width)
    {
        this.icon = icon; this.iconColor = iconColor; this.title = title; this.desc = desc; this.right = right;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(width, string.IsNullOrEmpty(desc) ? 48 : 58);
        Margin = new Padding(0, 0, 0, 4);
        BackColor = Theme.Bg;
        if (right != null)
        {
            // Keeps a control whose size changes (an auto-sizing panel settling on adding, or a button whose text
            // changes) right-aligned and centred, and re-trims the text beside it.
            Action place = () => { right.Location = new Point(Width - right.Width - 16, (Height - right.Height) / 2); Invalidate(); };
            right.SizeChanged += (s, e) => place();
            right.Anchor = AnchorStyles.Right;
            Controls.Add(right);
            place();
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        Theme.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 8, Theme.Card);
        float x = 16;
        if (icon != null) { icon(g, new RectangleF(x, (Height - 18) / 2f, 18, 18), iconColor); x += 34; }
        var f = Theme.UI(10f); var sf = Theme.UI(8.5f);
        using (var tb = new SolidBrush(Theme.Text)) using (var sb = new SolidBrush(Theme.Sub))
        {
            // Text stops short of the control on the right, ending in "…" if it doesn't fit.
            float w = Math.Max(10, (right != null ? right.Left - 12 : Width - 16) - x);
            if (string.IsNullOrEmpty(desc)) g.DrawString(title, f, tb, new RectangleF(x, (Height - f.GetHeight(g)) / 2, w, f.GetHeight(g) + 2), Clip);
            else { g.DrawString(title, f, tb, new RectangleF(x, 9, w, 22), Clip); g.DrawString(desc, sf, sb, new RectangleF(x, 31, w, 18), Clip); }
        }
    }
}

// Stops a scrolling page from jumping back to the top when a tall child (like the arrange list) gets focus.
class NoJumpFlow : FlowLayoutPanel
{
    protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
}

// The Arrange list: one self-drawn control (no child windows) where rows follow the mouse while dragging,
// the others slide out of the way, and the page scrolls when dragging near its edges.
// Keyboard: ↑/↓ select, Alt+↑/↓ (or Ctrl) move, Space toggles, Delete removes a divider.
class ReorderList : Control
{
    const int RowH = 48, Gap = 4, Step = RowH + Gap;
    readonly Settings c;
    readonly List<string> items;
    readonly Dictionary<string, float> pos = new Dictionary<string, float>(); // animated top of each row
    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 15 };
    int hover = -1; string hoverPart = "";
    int press = -1; string pressPart = ""; Point pressAt;
    int drag = -1, target; float dragY, grabDy;
    int selected = -1;

    public event Action<List<string>> Moved;
    public event Action<string> Toggled, Removed;
    public event Action<string, Rectangle> IconClicked;

    readonly Func<string, bool> isOn;
    readonly Action<string, bool> setOn;
    readonly string dividerDesc;

    public ReorderList(Settings c, List<string> items, int width, Func<string, bool> isOn, Action<string, bool> setOn, string dividerDesc)
    {
        this.c = c; this.items = items; this.isOn = isOn; this.setOn = setOn; this.dividerDesc = dividerDesc;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Bg; TabStop = true;
        for (int i = 0; i < items.Count; i++) pos[items[i]] = i * Step;
        Size = new Size(width, ListHeight());
        anim.Tick += (s, e) => Animate();
    }

    protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

    int ListHeight() { return Math.Max(RowH, items.Count * Step - Gap); }
    public string SelectedId { get { return selected >= 0 && selected < items.Count ? items[selected] : null; } }
    public int RowTop(string id) { return Math.Max(0, items.IndexOf(id)) * Step; }
    public void Select(string id) { selected = items.IndexOf(id); Invalidate(); }

    // ---- geometry
    float TargetY(int i)
    {
        if (drag < 0) return i * Step;
        if (i == drag) return dragY;
        int k = i > drag ? i - 1 : i; // index with the dragged row taken out
        if (k >= target) k++;
        return k * Step;
    }

    Rectangle IconRect(float y) { return new Rectangle(30, (int)y + 10, 28, 28); }
    Rectangle DownRect(float y) { return new Rectangle(Width - 12 - 28, (int)y + 10, 28, 28); }
    Rectangle UpRect(float y) { return new Rectangle(Width - 12 - 58, (int)y + 10, 28, 28); }
    Rectangle SwitchRect(float y, bool divider)
    {
        return divider ? new Rectangle(Width - 12 - 58 - 14 - 28, (int)y + 10, 28, 28) : new Rectangle(Width - 12 - 58 - 14 - 44, (int)y + 13, 44, 22);
    }

    int IndexAt(int y)
    {
        if (drag >= 0 && y >= dragY && y < dragY + RowH) return drag;
        for (int i = 0; i < items.Count; i++)
        {
            float p = pos[items[i]];
            if (y >= p && y < p + RowH) return i;
        }
        return -1;
    }

    string PartAt(int i, Point pt)
    {
        if (i < 0) return "";
        float y = pos[items[i]];
        bool divider = Settings.IsDivider(items[i]);
        if (DownRect(y).Contains(pt)) return i < items.Count - 1 ? "down" : "none";
        if (UpRect(y).Contains(pt)) return i > 0 ? "up" : "none";
        if (SwitchRect(y, divider).Contains(pt)) return divider ? "remove" : "toggle";
        if (IconRect(y).Contains(pt)) return "icon";
        return "body";
    }

    // ---- animation and auto-scroll
    void StartAnim() { if (!anim.Enabled) anim.Start(); }

    void Animate()
    {
        bool moving = false;
        for (int i = 0; i < items.Count; i++)
        {
            float t = TargetY(i), p = pos[items[i]];
            if (i == drag || Math.Abs(t - p) < 0.5f) pos[items[i]] = t;
            else { pos[items[i]] = p + (t - p) * 0.35f; moving = true; }
        }
        if (drag >= 0) AutoScroll();
        else if (!moving) anim.Stop();
        Invalidate();
    }

    void AutoScroll()
    {
        var p = Parent as ScrollableControl;
        if (p == null) return;
        var pt = p.PointToClient(MousePosition);
        int dy = pt.Y < 40 ? -14 : pt.Y > p.ClientSize.Height - 40 ? 14 : 0;
        if (dy == 0) return;
        int before = -p.AutoScrollPosition.Y;
        p.AutoScrollPosition = new Point(0, Math.Max(0, before + dy));
        if (-p.AutoScrollPosition.Y != before) UpdateDrag(PointToClient(MousePosition).Y);
    }

    void EnsureVisible(int i)
    {
        var p = Parent as ScrollableControl;
        if (p == null || i < 0) return;
        int top = Top + i * Step, bottom = top + RowH, scroll = -p.AutoScrollPosition.Y;
        if (top < 0) p.AutoScrollPosition = new Point(0, Math.Max(0, scroll + top - 8));
        else if (bottom > p.ClientSize.Height) p.AutoScrollPosition = new Point(0, scroll + bottom - p.ClientSize.Height + 8);
    }

    // ---- actions
    public void MoveTo(int from, int to)
    {
        to = Math.Max(0, Math.Min(items.Count - 1, to));
        selected = to;
        StartAnim();
        if (from == to) return;
        var id = items[from];
        items.RemoveAt(from); items.Insert(to, id);
        if (Moved != null) Moved(new List<string>(items));
    }

    void Activate(int i, string part)
    {
        var id = items[i];
        switch (part)
        {
            case "toggle":
                setOn(id, !isOn(id));
                Invalidate();
                if (Toggled != null) Toggled(id);
                break;
            case "up": MoveTo(i, i - 1); EnsureVisible(selected); break;
            case "down": MoveTo(i, i + 1); EnsureVisible(selected); break;
            case "remove":
                items.RemoveAt(i); pos.Remove(id);
                selected = Math.Min(i, items.Count - 1);
                Height = ListHeight();
                StartAnim();
                if (Removed != null) Removed(id);
                break;
            case "icon":
                if (IconClicked != null) IconClicked(id, RectangleToScreen(IconRect(pos[id])));
                break;
        }
    }

    void UpdateDrag(int mouseY)
    {
        dragY = Math.Max(0, Math.Min((items.Count - 1) * Step, mouseY - grabDy));
        target = Math.Max(0, Math.Min(items.Count - 1, (int)Math.Round(dragY / Step)));
        StartAnim();
    }

    // ---- mouse
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left)
        {
            press = IndexAt(e.Y);
            pressPart = PartAt(press, e.Location);
            pressAt = e.Location;
            if (press >= 0) { selected = press; Invalidate(); }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag >= 0) { UpdateDrag(e.Y); return; }
        if (press >= 0 && pressPart == "body" && e.Button == MouseButtons.Left && (Math.Abs(e.Y - pressAt.Y) > 4 || Math.Abs(e.X - pressAt.X) > 4))
        {
            drag = press; target = press;
            grabDy = pressAt.Y - pos[items[drag]];
            Cursor = Cursors.SizeNS;
            UpdateDrag(e.Y);
            return;
        }
        int i = IndexAt(e.Y);
        string part = PartAt(i, e.Location);
        if (i != hover || part != hoverPart) { hover = i; hoverPart = part; Invalidate(); }
        Cursor = part == "body" ? Cursors.SizeNS : part == "" || part == "none" ? Cursors.Default : Cursors.Hand;
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (drag >= 0)
        {
            int from = drag;
            drag = -1;
            Cursor = Cursors.SizeNS;
            MoveTo(from, target);
        }
        else if (press >= 0 && e.Button == MouseButtons.Left && IndexAt(e.Y) == press && PartAt(press, e.Location) == pressPart)
            Activate(press, pressPart);
        press = -1;
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; hoverPart = ""; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var p = Parent as ScrollableControl;
        if (p != null) p.AutoScrollPosition = new Point(0, Math.Max(0, -p.AutoScrollPosition.Y - e.Delta / 120 * Step));
        var h = e as HandledMouseEventArgs;
        if (h != null) h.Handled = true;
    }

    // ---- keyboard
    protected override bool IsInputKey(Keys k)
    {
        var key = k & Keys.KeyCode;
        return key == Keys.Up || key == Keys.Down || key == Keys.Space || key == Keys.Delete || base.IsInputKey(k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (items.Count > 0 && drag < 0)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                int d = e.KeyCode == Keys.Up ? -1 : 1;
                if (selected < 0) selected = 0;
                else if (e.Alt || e.Control) MoveTo(selected, selected + d);
                else selected = Math.Max(0, Math.Min(items.Count - 1, selected + d));
                EnsureVisible(selected);
                Invalidate();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Space && selected >= 0 && !Settings.IsDivider(items[selected])) { Activate(selected, "toggle"); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && selected >= 0 && Settings.IsDivider(items[selected])) { Activate(selected, "remove"); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    // ---- painting
    static readonly string[] dividerStyles = { "Line", "Dotted line", "Dot", "Blank space" };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Bg);
        if (drag >= 0)
            using (var p = new Pen(Color.FromArgb(150, Theme.Accent), 1.5f) { DashStyle = DashStyle.Dash })
            using (var path = Icons.Round(new RectangleF(1, target * Step + 1, Width - 3, RowH - 2), 8))
                g.DrawPath(p, path);
        for (int i = 0; i < items.Count; i++) if (i != drag) DrawRow(g, i);
        if (drag >= 0) DrawRow(g, drag);
    }

    void DrawRow(Graphics g, int i)
    {
        string id = items[i];
        var info = Stats.Get(id, c);
        bool divider = Settings.IsDivider(id), on = isOn(id), lifted = i == drag, hot = i == hover && drag < 0;
        float y = pos[id];
        var r = new RectangleF(0, y, Width - 1, RowH);
        if (lifted) Theme.FillRound(g, new RectangleF(3, y + 4, Width - 4, RowH), 8, Color.FromArgb(120, 0, 0, 0));
        Theme.FillRound(g, r, 8, lifted ? Theme.CardHover : hot ? Color.FromArgb(42, 42, 49) : Theme.Card);
        if (lifted || (Focused && i == selected)) Theme.StrokeRound(g, r, 8, Theme.Accent, 1.5f);

        Icons.Grip(g, new RectangleF(8, y + (RowH - 16) / 2f, 16, 16), Theme.Sub);

        // Icon (click to change it)
        var ir = IconRect(y);
        bool iconHot = hot && hoverPart == "icon";
        if (iconHot) Theme.FillRound(g, ir, 6, Theme.Border);
        else if (!divider) Theme.StrokeRound(g, ir, 6, Color.FromArgb(70, Theme.Border), 1f);
        var icol = on ? info.Color : Color.FromArgb(90, info.Color);
        var inner = new RectangleF(ir.X + 5, ir.Y + 5, 18, 18);
        if (info.TextLabel)
        {
            var f = Theme.UI(7f, FontStyle.Bold);
            var sz = g.MeasureString(info.Short, f);
            using (var b = new SolidBrush(icol)) g.DrawString(info.Short, f, b, ir.X + (ir.Width - sz.Width) / 2, ir.Y + (ir.Height - sz.Height) / 2);
        }
        else if (info.Icon != null) info.Icon(g, inner, icol);
        else using (var p = new Pen(Color.FromArgb(120, Theme.Sub), 1.5f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(p, inner.X + 2, inner.Y + 2, 14, 14);

        // Title and status
        var tf = Theme.UI(10f); var sf = Theme.UI(8.5f);
        string title = divider ? "Divider" : info.Title;
        string desc = divider ? (dividerDesc ?? dividerStyles[Math.Max(0, Math.Min(3, c.DividerStyle))] + " · separates groups") : on ? null : "Hidden";
        using (var tb = new SolidBrush(on ? Theme.Text : Theme.Sub)) using (var sb = new SolidBrush(Theme.Sub))
        {
            if (desc == null) g.DrawString(title, tf, tb, 68, y + (RowH - tf.GetHeight(g)) / 2);
            else { g.DrawString(title, tf, tb, 68, y + 6); g.DrawString(desc, sf, sb, 68, y + 26); }
        }

        // Switch / remove, then the move buttons
        var sr = SwitchRect(y, divider);
        if (divider)
        {
            if (hot && hoverPart == "remove") Theme.FillRound(g, sr, 6, Theme.Border);
            Theme.Cross(g, sr, hot && hoverPart == "remove" ? Theme.WarnText : Theme.Sub);
        }
        else Toggle.Draw(g, new RectangleF(sr.X + 1, sr.Y + 1, sr.Width - 3, sr.Height - 3), on, 255);
        DrawMove(g, UpRect(y), true, i > 0, hot && hoverPart == "up");
        DrawMove(g, DownRect(y), false, i < items.Count - 1, hot && hoverPart == "down");
    }

    static void DrawMove(Graphics g, Rectangle r, bool up, bool enabled, bool hot)
    {
        if (hot && enabled) Theme.FillRound(g, r, 6, Theme.Border);
        Theme.Chevron(g, r, up, enabled ? (hot ? Theme.Text : Theme.Sub) : Color.FromArgb(60, Theme.Sub));
    }
}

// Popup for choosing a stat's icon (preset, none or a short text label) and colour (swatch, custom or default).
class IconPicker : Control
{
    class Cell { public Rectangle R; public string Tip; public bool Selected; public Action<Graphics, Rectangle> Draw; public Action Click; }
    const int Cols = 8, CellS = 34, Pad = 10;
    static readonly Color[] Swatches =
    {
        Color.FromArgb(255, 95, 95), Color.FromArgb(255, 140, 80), Color.FromArgb(255, 200, 60), Color.FromArgb(240, 230, 120),
        Color.FromArgb(118, 200, 40), Color.FromArgb(90, 220, 130), Color.FromArgb(90, 220, 170), Color.FromArgb(80, 200, 255),
        Color.FromArgb(100, 160, 255), Color.FromArgb(130, 120, 255), Color.FromArgb(180, 130, 255), Color.FromArgb(240, 150, 200),
        Color.FromArgb(255, 255, 255), Color.FromArgb(170, 170, 180), Color.FromArgb(100, 100, 110),
    };
    readonly List<Cell> cells = new List<Cell>();
    readonly List<KeyValuePair<string, int>> headers = new List<KeyValuePair<string, int>>();
    readonly ToolTip tip = new ToolTip { InitialDelay = 300 };
    readonly Settings c; readonly string id;
    int hover = -1;
    public event Action Changed, CustomColour;

    public IconPicker(Settings c, string id)
    {
        this.c = c; this.id = id;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Theme.Card;
        Build();
    }

    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }

    void Add(Rectangle r, string tipText, bool selected, Action<Graphics, Rectangle> draw, Action click)
    {
        cells.Add(new Cell { R = r, Tip = tipText, Selected = selected, Draw = draw, Click = click });
    }

    void Build()
    {
        cells.Clear(); headers.Clear();
        int y = Pad, w = Cols * CellS;
        string cur = c.IconOf(id);
        var info = Stats.Get(id, c);
        Color col = info.Color;
        if (!Settings.IsDivider(id))
        {
            headers.Add(new KeyValuePair<string, int>("Icon", y)); y += 22;
            var labels = new[] { "Default", "No icon", "Text  " + info.Short };
            var values = new[] { "", "none", "text" };
            int bw = (w - 8) / 3;
            for (int i = 0; i < 3; i++)
            {
                string label = labels[i], v = values[i];
                Add(new Rectangle(Pad + i * (bw + 4), y, bw, 28), null, cur == v, (g, r) => DrawText(g, r, label), () => SetIcon(v));
            }
            y += 34;
            int n = 0;
            foreach (var kv in Icons.Presets)
            {
                string name = kv.Key; IconFn fn = kv.Value;
                Add(new Rectangle(Pad + n % Cols * CellS, y + n / Cols * CellS, CellS, CellS), name, cur == name,
                    (g, r) => fn(g, new RectangleF(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16), col), () => SetIcon(name));
                n++;
            }
            y += (n + Cols - 1) / Cols * CellS + 8;
        }
        headers.Add(new KeyValuePair<string, int>("Colour", y)); y += 22;
        var custom = c.ColorOf(id);
        for (int i = 0; i <= Swatches.Length; i++)
        {
            var r = new Rectangle(Pad + i % Cols * CellS, y + i / Cols * CellS, CellS, CellS);
            if (i < Swatches.Length)
            {
                Color sw = Swatches[i];
                Add(r, null, custom.HasValue && custom.Value.ToArgb() == sw.ToArgb(), (g, rr) => DrawSwatch(g, rr, sw), () => SetColour(sw));
            }
            else Add(r, "Custom colour…", custom.HasValue && !Swatches.Any(s => s.ToArgb() == custom.Value.ToArgb()), DrawCustom, () => { if (CustomColour != null) CustomColour(); });
        }
        y += (Swatches.Length + Cols) / Cols * CellS + 6;
        Add(new Rectangle(Pad, y, w, 28), null, !custom.HasValue, (g, r) => DrawText(g, r, "Default colour"), () => SetColour(null));
        y += 28;
        Size = new Size(w + Pad * 2, y + Pad);
    }

    void SetIcon(string v) { c.SetCustom(id, v, c.ColorOf(id)); Refresh2(); }
    void SetColour(Color? col) { c.SetCustom(id, c.IconOf(id), col); Refresh2(); }
    void Refresh2() { Build(); Invalidate(); if (Changed != null) Changed(); }

    static void DrawText(Graphics g, Rectangle r, string text)
    {
        var f = Theme.UI(9f);
        var sz = g.MeasureString(text, f);
        using (var b = new SolidBrush(Theme.Text)) g.DrawString(text, f, b, r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2);
    }

    static void DrawSwatch(Graphics g, Rectangle r, Color col)
    {
        Theme.FillRound(g, new RectangleF(r.X + 7, r.Y + 7, r.Width - 14, r.Height - 14), 5, col);
    }

    static void DrawCustom(Graphics g, Rectangle r)
    {
        Icons.Palette(g, new RectangleF(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16), Theme.Sub);
    }

    int CellAt(Point pt) { return cells.FindIndex(x => x.R.Contains(pt)); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = CellAt(e.Location);
        if (i != hover)
        {
            hover = i; Invalidate();
            tip.SetToolTip(this, i >= 0 ? cells[i].Tip : null);
        }
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int i = CellAt(e.Location);
        if (e.Button == MouseButtons.Left && i >= 0) cells[i].Click();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Card);
        using (var b = new SolidBrush(Theme.Sub))
            foreach (var h in headers) g.DrawString(h.Key, Theme.UI(8.5f, FontStyle.Bold), b, Pad - 2, h.Value);
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var rr = new RectangleF(cell.R.X + 1, cell.R.Y + 1, cell.R.Width - 2, cell.R.Height - 2);
            if (i == hover) Theme.FillRound(g, rr, 6, Theme.Border);
            else if (cell.R.Height == 28) Theme.FillRound(g, rr, 6, Theme.CardHover);
            if (cell.Selected) Theme.StrokeRound(g, rr, 6, Theme.Accent, 1.8f);
            cell.Draw(g, cell.R);
        }
    }
}

class SettingsForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    readonly StatsBar bar;
    readonly Settings c;
    readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly Panel nav = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Theme.Nav };
    readonly PictureBox preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.FromArgb(32, 32, 36) };
    readonly System.Windows.Forms.Timer previewTimer = new System.Windows.Forms.Timer { Interval = 1000 };
    readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 400 };
    readonly List<Label> navItems = new List<Label>();
    const int RowW = 540;
    string page = "Stats";
    ReorderList arrangeList;
    string pendingSelect;
    int gpuCount = -1;

    public SettingsForm(StatsBar bar)
    {
        this.bar = bar; c = bar.Cfg;
        Text = "Kinetik";
        Icon = TrayIconArt.Frame(c.TrayStyle, SystemInformation.IconSize.Width, 0);
        ClientSize = new Size(800, 640);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; ForeColor = Theme.Text;
        Font = Theme.UI(9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
            if (st != null) Icon = new Icon(st);

        // Preview strip on top (looks like a slice of taskbar)
        var top = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Bg, Padding = new Padding(16, 14, 16, 10) };
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 36) };
        frame.Controls.Add(preview);
        top.Controls.Add(frame);

        // Navigation
        var brand = new Label { Text = "Kinetik", Font = Theme.UI(13f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = false, Height = 64, Dock = DockStyle.Top, Padding = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        var version = new Label { Text = "v" + VersionText, Font = Theme.UI(8.5f), ForeColor = Theme.Sub, AutoSize = false, Height = 36, Dock = DockStyle.Bottom, Padding = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        var navList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10, 0, 10, 0), BackColor = Theme.Nav };
        foreach (var p in new[] { "Stats", "Arrange", "Appearance", "Colours", "Widget", "Overlay", "General", "About" }) navList.Controls.Add(NavItem(p));
        nav.Controls.Add(navList); nav.Controls.Add(brand); nav.Controls.Add(version);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        right.Controls.Add(content); right.Controls.Add(top);
        Controls.Add(right); Controls.Add(nav);

        previewTimer.Tick += (s, e) => { UpdatePreview(); CheckGpuList(); };
        previewTimer.Start();
        saveTimer.Tick += (s, e) => { saveTimer.Stop(); c.Save(); };
        FormClosing += (s, e) => { if (saveTimer.Enabled) { saveTimer.Stop(); c.Save(); } };
        FormClosed += (s, e) =>
        {
            previewTimer.Dispose(); saveTimer.Dispose();
            if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
        };
        ShowPage(page);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int one = 1; DwmSetWindowAttribute(Handle, 20, ref one, 4); // dark title bar
        int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); // rounded corners
    }

    static readonly Dictionary<string, IconFn> navIcons = new Dictionary<string, IconFn>
    {
        { "Stats", Icons.Chip }, { "Arrange", Icons.Grip }, { "Appearance", Icons.Text }, { "Colours", Icons.Palette }, { "Widget", Icons.Monitor }, { "Overlay", Icons.Gamepad }, { "General", Icons.Gear }, { "About", Icons.Info }
    };

    Label NavItem(string name)
    {
        var l = new Label { Text = name, Tag = name, AutoSize = false, Size = new Size(180, 40), Margin = new Padding(0, 0, 0, 4), Cursor = Cursors.Hand, Font = Theme.UI(10f) };
        l.Paint += (s, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            bool sel = (string)l.Tag == page;
            g.Clear(Theme.Nav);
            if (sel)
            {
                Theme.FillRound(g, new RectangleF(0, 0, l.Width - 1, l.Height - 1), 6, Theme.Card);
                Theme.FillRound(g, new RectangleF(0, 12, 3, l.Height - 24), 1.5f, Theme.Accent);
            }
            navIcons[name](g, new RectangleF(14, 11, 18, 18), sel ? Theme.Accent : Theme.Sub);
            using (var b = new SolidBrush(sel ? Theme.Text : Theme.Sub)) g.DrawString(name, l.Font, b, 44, (l.Height - l.Font.GetHeight(g)) / 2);
        };
        l.Click += (s, e) => ShowPage(name);
        navItems.Add(l);
        return l;
    }

    // Changes show immediately; writing to the registry waits until the user pauses (e.g. while dragging a slider).
    void Apply()
    {
        bar.Render(true);
        UpdatePreview();
        saveTimer.Stop(); saveTimer.Start();
    }

    void UpdatePreview()
    {
        var old = preview.Image;
        preview.Image = bar.RenderBitmap(Math.Max(40, bar.TaskbarHeight), false);
        if (old != null) old.Dispose();
    }

    // The AMD / Intel GPU list arrives a moment after the Stats page asks for it.
    void CheckGpuList()
    {
        if (page == "Stats" && gpuCount >= 0 && bar.Sampler.GpuNames.Count != gpuCount && !(ActiveControl is TextBox)) RefreshPage();
    }

    // ---- page building helpers ----
    FlowLayoutPanel list;

    void Header(string text)
    {
        list.Controls.Add(new Label { Text = text, Font = Theme.UI(10f, FontStyle.Bold), ForeColor = Theme.Sub, AutoSize = false, Size = new Size(RowW, 36), TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(2, 6, 0, 6) });
    }

    void Hint(string text, int height = 40)
    {
        list.Controls.Add(new Label { Text = text, Font = Theme.UI(9.5f), ForeColor = Theme.Sub, AutoSize = false, Size = new Size(RowW, height), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2, 6, 0, 4) });
    }

    void Add(IconFn icon, Color iconColor, string title, string desc, Control right)
    {
        list.Controls.Add(new Row(icon, iconColor, title, desc, right, RowW));
    }

    void Switch(IconFn icon, Color iconColor, string title, string field, string desc = null)
    {
        var f = Settings.Field(field);
        var t = new Toggle { On = (bool)f.GetValue(c) };
        t.Changed += (s, e) => { f.SetValue(c, t.On); Apply(); };
        Add(icon, iconColor, title, desc, t);
    }

    void SliderRow(IconFn icon, Color iconColor, string title, string field, int min, int max, string unit, string desc = null)
    {
        var f = Settings.Field(field);
        var s = new Slider(min, max, (int)f.GetValue(c)) { Unit = unit };
        s.ValueChanged += (o, e) => { f.SetValue(c, s.Value); Apply(); };
        Add(icon, iconColor, title, desc, s);
    }

    void ColourRow(IconFn icon, string title, string field)
    {
        var f = Settings.Field(field);
        var sw = new Swatch { Value = (Color)f.GetValue(c) };
        sw.Click += (s, e) =>
        {
            using (var dlg = new ColorDialog { Color = sw.Value, FullOpen = true })
                if (dlg.ShowDialog(this) == DialogResult.OK) { f.SetValue(c, dlg.Color); sw.Value = dlg.Color; Apply(); RefreshPage(); }
        };
        Add(icon, (Color)f.GetValue(c), title, null, sw);
    }

    Button Btn(string text, EventHandler click, bool accent = false)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = accent ? Theme.Accent : Theme.CardHover, ForeColor = accent ? Theme.Bg : Theme.Text, Padding = new Padding(10, 3, 10, 3), Font = Theme.UI(9.5f)
        };
        b.FlatAppearance.BorderColor = accent ? Theme.Accent : Theme.Border;
        b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(110, 210, 255) : Theme.Border;
        if (click != null) b.Click += click;
        return b;
    }

    ComboBox Combo(string[] items, int selected, Action<int> changed, int width = 170)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardHover, ForeColor = Theme.Text, Width = width, Font = Theme.UI(9.5f) };
        cb.Items.AddRange(items);
        cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
        cb.SelectedIndexChanged += (s, e) => changed(cb.SelectedIndex);
        return cb;
    }

    public void ShowPage(string name)
    {
        if (!navIcons.ContainsKey(name)) name = "Stats";
        page = name;
        foreach (var l in navItems) l.Invalidate();
        content.SuspendLayout();
        foreach (Control ctl in content.Controls.Cast<Control>().ToList()) ctl.Dispose();
        arrangeList = null; gpuCount = -1;
        list = new NoJumpFlow { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 0, 16, 16), BackColor = Theme.Bg };
        content.Controls.Add(list);

        if (name == "Stats") BuildStats();
        else if (name == "Arrange") BuildArrange();
        else if (name == "Appearance") BuildAppearance();
        else if (name == "Colours") BuildColours();
        else if (name == "Widget") BuildWidget();
        else if (name == "Overlay") BuildOverlay();
        else if (name == "General") BuildGeneral();
        else BuildAbout();

        content.ResumeLayout();
        UpdatePreview();
    }

    public void RefreshIfShowing(string name) { if (page == name) RefreshPage(); }

    // Rebuilds the current page without losing the scroll position.
    void RefreshPage()
    {
        int scroll = list == null ? 0 : -list.AutoScrollPosition.Y;
        ShowPage(page);
        list.AutoScrollPosition = new Point(0, scroll);
        if (arrangeList != null && pendingSelect != null)
        {
            arrangeList.Select(pendingSelect);
            int rowY = arrangeList.Top - list.AutoScrollPosition.Y + arrangeList.RowTop(pendingSelect);
            list.AutoScrollPosition = new Point(0, Math.Max(0, rowY - list.ClientSize.Height / 2));
            arrangeList.Focus();
            pendingSelect = null;
        }
    }

    void BuildStats()
    {
        Header("Network");
        Stat("Up"); Stat("Down");
        Stat("NetTotal", "Upload and download added together");
        Stat("Wifi", "Signal strength of the Wi-Fi network you're connected to");
        Stat("NetApp", Program.IsAdmin ? "The app using the most network right now" : "Needs admin – see General → Run as administrator");
        Stat("Vpn", "On while a VPN connection is active");
        Stat("PublicIp", "Your internet address. Asks api.ipify.org every 5 minutes while it's shown.");
        Stat("Ping", "Round-trip time to the address below");
        var host = new TextBox { Text = c.PingHost, Width = 170, BackColor = Theme.CardHover, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI(9.5f) };
        Action commit = () => { var v = host.Text.Trim(); if (v != "" && v != c.PingHost) { c.PingHost = v; Apply(); } };
        host.Leave += (s, e) => commit();
        host.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { commit(); e.SuppressKeyPress = true; } };
        Add(Icons.Globe, c.UpColor, "Ping address", "A website or IP address, e.g. 1.1.1.1 or google.com", host);

        Header("Processor");
        Stat("Cpu");
        Stat("CpuCores", "A tiny usage bar for every logical core");
        Stat("CpuClock");
        Stat("CpuTemp", !Program.IsAdmin ? "Needs admin for an exact reading – see General → Run as administrator"
            : Program.PawnIOInstalled ? "Via LibreHardwareMonitor sensors" : "Needs the PawnIO driver for an exact reading – see General");
        Stat("CpuPower", "Package power in watts");

        Header("Memory");
        Stat("Ram"); Stat("RamGb");
        Stat("RamCommit", "Memory promised to apps, including the page file");
        Stat("RamTemp", Program.IsAdmin ? "Hottest memory module, if your RAM reports it" : "Needs admin");

        Header("Graphics");
        bar.Sampler.RequestGpuScan();
        var gpus = bar.Sampler.GpuNames;
        gpuCount = gpus.Count;
        if (gpus.Count > 0)
        {
            var choices = new List<string> { "Auto" };
            choices.AddRange(gpus);
            int sel = Math.Max(0, choices.IndexOf(c.GpuSource));
            Add(Icons.Gpu, c.GpuColor, "Graphics card", "NVIDIA, AMD and Intel supported",
                Combo(choices.ToArray(), sel, i => { c.GpuSource = i == 0 ? "" : choices[i]; Apply(); }, 230));
        }
        else Add(Icons.Gpu, c.GpuColor, "Looking for graphics cards…", null, null);
        Stat("Gpu"); Stat("GpuTemp");
        Stat("GpuHotspot", "The hottest point on the GPU. Not every card reports it.");
        Stat("GpuMemTemp", "Video memory temperature. Not every card reports it.");
        Stat("GpuClock", "Core clock in MHz");
        Stat("GpuFan", "Percent, or RPM when that's all the card reports");
        Stat("GpuVram"); Stat("GpuVramPct"); Stat("GpuPower");
        Stat("Fps", Program.IsAdmin ? "Frame rate of the app in front, including generated frames (DirectX, OpenGL and Vulkan)" : "Needs admin – see General → Run as administrator");
        Stat("BaseFps", Program.IsAdmin ? "Frames the game renders itself, before DLSS / FSR frame generation. Games with NVIDIA Reflex." : "Needs admin – see General → Run as administrator");
        Stat("FpsLow", Program.IsAdmin ? "The frame rate 99% of frames beat over the last 10 seconds. Shows stutter that averages hide." : "Needs admin");
        Stat("FpsLow01", Program.IsAdmin ? "The same for 99.9% of frames: the worst hitches" : "Needs admin");
        Stat("FrameTime", Program.IsAdmin ? "Time per frame, with a graph of the last 3 seconds. Spikes are stutters." : "Needs admin");
        Stat("Latency", Program.IsAdmin ? "Time from the game starting a frame to presenting it. Games with NVIDIA Reflex." : "Needs admin");

        Header("Storage");
        Stat("Disk", "How busy the drives are");
        Stat("DiskRead"); Stat("DiskWrite");
        Stat("DiskFree", "Turns to the warning colour when space runs low");
        Stat("SsdTemp", Program.IsAdmin ? "The hottest of your drives" : "Needs admin");
        var drives = new List<string>();
        try { drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name).ToList(); } catch { }
        var driveChoices = new List<string> { "Windows drive" };
        driveChoices.AddRange(drives);
        Add(Icons.Disk, c.DiskColor, "Drive for free space", null,
            Combo(driveChoices.ToArray(), c.FreeDrive == "" ? 0 : driveChoices.IndexOf(c.FreeDrive), i => { c.FreeDrive = i == 0 ? "" : driveChoices[i]; Apply(); }, 150));

        Header("System");
        Stat("Processes");
        Stat("Uptime", "Time since Windows started");
        Stat("Clock"); Stat("Date");

        Header("Batteries");
        Stat("Batteries", "Headphones, mice, keyboards, controllers");
        Switch(Icons.Text, c.BatColor, "Show device names", "DeviceNames");
        Stat("PcBattery", "Laptops only");
        Stat("BatTime", "Laptops only, while unplugged");
    }

    void Stat(string id, string desc = null)
    {
        var info = Stats.Get(id, c);
        Switch(info.Icon ?? info.Default, info.Color, info.Title, id, desc);
    }

    // ---- Arrange: reorder items, switch them on/off, give each its own icon/colour, and add dividers.
    void BuildArrange()
    {
        Hint("Drag a row to move it (left → right on the taskbar), or select it and press Alt+↑/↓. Click an icon to change it or give that stat its own colour.", 48);

        var tools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, 8) };
        var addBtn = Btn("+  Add divider", (s, e) =>
        {
            var id = c.NewDividerId();
            var o = c.OrderList();
            int at = arrangeList != null && arrangeList.SelectedId != null ? o.IndexOf(arrangeList.SelectedId) + 1 : o.Count;
            o.Insert(at, id);
            c.Order = string.Join(",", o);
            Apply();
            pendingSelect = id;
            RefreshPage();
        }, true);
        addBtn.Margin = new Padding(0, 0, 8, 0);
        var resetBtn = Btn("Reset order", (s, e) =>
        {
            if (MessageBox.Show(this, "Put every stat back in its original order and remove all dividers?", "Kinetik", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            c.Order = Settings.DefaultOrder;
            Apply(); RefreshPage();
        });
        tools.Controls.Add(addBtn); tools.Controls.Add(resetBtn);
        list.Controls.Add(tools);

        var showOff = new Toggle { On = c.ArrangeShowOff };
        showOff.Changed += (s, e) => { c.ArrangeShowOff = showOff.On; Apply(); RefreshPage(); };
        Add(Icons.List, Theme.Accent, "Show switched-off stats", "Turn off to see only what's on the taskbar", showOff);

        var visible = c.OrderList().Where(id => c.ArrangeShowOff || c.IsOn(id)).ToList();
        arrangeList = new ReorderList(c, visible, RowW, c.IsOn, (id, on) => Settings.Field(id).SetValue(c, on), null) { Margin = new Padding(0, 4, 0, 0) };
        arrangeList.Moved += order =>
        {
            // Hidden items keep their slots; the visible ones fill theirs in the new order.
            var set = new HashSet<string>(order);
            int k = 0;
            c.Order = string.Join(",", c.OrderList().Select(id => set.Contains(id) ? order[k++] : id));
            Apply();
        };
        arrangeList.Toggled += id => Apply();
        arrangeList.Removed += id =>
        {
            c.Order = string.Join(",", c.OrderList().Where(x => x != id));
            c.SetCustom(id, "", null);
            Apply();
        };
        arrangeList.IconClicked += ShowIconPicker;
        list.Controls.Add(arrangeList);
    }

    void ShowIconPicker(string id, Rectangle screen)
    {
        var picker = new IconPicker(c, id);
        var host = new ToolStripControlHost(picker) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = picker.Size };
        var dd = new ToolStripDropDown { Padding = new Padding(1), BackColor = Theme.Card, Renderer = new DarkMenuRenderer() };
        dd.Items.Add(host);
        picker.Changed += () => { Apply(); if (arrangeList != null) arrangeList.Invalidate(); };
        picker.CustomColour += () =>
        {
            dd.Close();
            BeginInvoke((Action)(() =>
            {
                var cur = c.ColorOf(id);
                using (var dlg = new ColorDialog { Color = cur.HasValue ? cur.Value : Stats.Get(id, c).Color, FullOpen = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        c.SetCustom(id, c.IconOf(id), dlg.Color);
                        Apply();
                        if (arrangeList != null) arrangeList.Invalidate();
                    }
            }));
        };
        dd.Closed += (s, e) => BeginInvoke((Action)dd.Dispose);
        var wa = Screen.FromRectangle(screen).WorkingArea;
        int x = Math.Min(screen.Left, wa.Right - picker.Width - 4);
        int y = screen.Bottom + 4 + picker.Height > wa.Bottom ? screen.Top - picker.Height - 6 : screen.Bottom + 4;
        dd.Show(new Point(x, y));
    }

    void BuildAppearance()
    {
        Header("Text");
        var fontBtn = Btn(c.FontName, null);
        fontBtn.Click += (s, e) =>
        {
            using (var cur = new Font(c.FontName, c.FontSize > 0 ? c.FontSize : 10f))
            using (var dlg = new FontDialog { Font = cur, ShowEffects = false, FontMustExist = true, AllowVerticalFonts = false })
                if (dlg.ShowDialog(this) == DialogResult.OK) { c.FontName = dlg.Font.Name; Apply(); RefreshPage(); }
        };
        Add(Icons.Text, Theme.Accent, "Font", "Monospace fonts keep the numbers from shifting", fontBtn);
        var sizes = new[] { 0f, 8, 9, 10, 11, 12, 13, 14, 16, 18 };
        Add(Icons.Text, Theme.Accent, "Font size", null,
            Combo(sizes.Select(v => v == 0 ? "Auto (fit taskbar)" : v + " pt").ToArray(), Array.IndexOf(sizes, c.FontSize), i => { c.FontSize = sizes[i]; Apply(); }));

        Header("Layout");
        Add(Icons.Grid, Theme.Accent, "Rows", null, Combo(new[] { "One line", "Two lines (compact)" }, c.TwoLines ? 1 : 0, i => { c.TwoLines = i == 1; Apply(); }));

        Header("Spacing");
        SliderRow(Icons.Spacing, Theme.Accent, "Between stats", "GapStats", 0, 300, "%");
        SliderRow(Icons.Spacing, Theme.Accent, "Between icon and value", "GapIcon", 0, 300, "%");
        SliderRow(Icons.Spacing, Theme.Accent, "Edge padding", "EdgePad", 0, 300, "%", "Space at each end of the bar");
        SliderRow(Icons.Chip, Theme.Accent, "Icon size", "IconScale", 50, 200, "%");
        SliderRow(Icons.Grid, Theme.Accent, "Line spacing", "RowGap", -6, 12, " px", "Two-line layout only");
        Add(Icons.Gear, Theme.Sub, "Reset spacing", null, Btn("Reset", (s, e) => { c.ResetSpacing(); Apply(); RefreshPage(); }));

        Header("Dividers");
        Add(Icons.Divider, Theme.Accent, "Style", "Add dividers on the Arrange page", Combo(new[] { "Line", "Dotted line", "Dot", "Blank space" }, c.DividerStyle, i => { c.DividerStyle = i; Apply(); }, 150));
        SliderRow(Icons.Divider, Theme.Accent, "Divider height", "DividerHeight", 20, 160, "%");

        Header("Hover");
        Switch(Icons.Pulse, Theme.Accent, "Hover graphs", "BarHover", "Hover over a stat for its last minute, its range and the top processes behind it");

        Header("Animation");
        Switch(Icons.Fan, Theme.Accent, "Animated icons", "BarAnimate", "Icons move with activity: fans spin, arrows flow, temps rise");
        var rates = new[] { 15, 30, 60 };
        Add(Icons.Gauge, Theme.Accent, "Animation frame rate", "Smoother looks nicer; lower uses less CPU. Shared with the widget.",
            Combo(new[] { "15 fps (lightest)", "30 fps", "60 fps (smoothest)" }, Math.Max(0, Array.IndexOf(rates, c.AnimFps)), i => { c.AnimFps = rates[i]; Apply(); }, 170));

        Header("Background");
        Switch(Icons.Palette, Theme.Accent, "Background pill", "Pill", "Rounded, see-through backdrop behind the stats");
        SliderRow(Icons.Palette, Theme.Accent, "Background opacity", "PillOpacity", 0, 100, "%");
        SliderRow(Icons.Palette, Theme.Accent, "Corner roundness", "PillRound", 0, 200, "%");
    }

    void BuildColours()
    {
        Hint("Tip: to give one stat its own colour, click its icon on the Arrange page.");
        Header("Text");
        Switch(Icons.Text, Theme.Accent, "Match taskbar theme", "AutoTheme", "White on dark taskbars, black on light. Turn off to pick your own.");
        ColourRow(Icons.Text, "Value text", "ValueColor");
        ColourRow(Icons.Text, "Secondary text and dividers", "LabelColor");
        ColourRow(Icons.Thermo, "Warning (see General → Warnings)", "WarnColor");
        Header("Icons");
        ColourRow(Icons.Up, "Upload and ping", "UpColor");
        ColourRow(Icons.Down, "Download", "DownColor");
        ColourRow(Icons.Chip, "CPU", "CpuColor");
        ColourRow(Icons.Thermo, "CPU temperature", "TempColor");
        ColourRow(Icons.Ram, "RAM", "RamColor");
        ColourRow(Icons.Gpu, "GPU", "GpuColor");
        ColourRow(Icons.Disk, "Disk", "DiskColor");
        ColourRow(Icons.Headphones, "Batteries", "BatColor");
        ColourRow(Icons.Clock, "Other (processes, uptime)", "MiscColor");
        Header("Background");
        ColourRow(Icons.Palette, "Pill colour", "PillColor");
    }

    static string VersionText { get { return Updates.Current; } }

    // The app icon's frames are PNG-compressed, which Icon.ToBitmap garbles, so decode the PNG frame directly.
    static Bitmap LoadLogo(int size)
    {
        try
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
            {
                if (st == null) return null;
                var b = new byte[st.Length];
                st.Read(b, 0, b.Length);
                int count = BitConverter.ToUInt16(b, 4), best = -1, bestSize = 0;
                for (int i = 0; i < count; i++)
                {
                    int w = b[6 + 16 * i] == 0 ? 256 : b[6 + 16 * i];
                    if (best < 0 || (w >= size ? (bestSize < size || w < bestSize) : w > bestSize && bestSize < size)) { best = i; bestSize = w; }
                }
                int len = BitConverter.ToInt32(b, 6 + 16 * best + 8), off = BitConverter.ToInt32(b, 6 + 16 * best + 12);
                using (var ms = new MemoryStream(b, off, len))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
        }
        catch { return null; }
    }

    void BuildAbout()
    {
        var logo = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(2, 24, 0, 8), BackColor = Theme.Bg };
        logo.Image = LoadLogo(128);
        logo.Disposed += (s, e) => { if (logo.Image != null) logo.Image.Dispose(); };
        list.Controls.Add(logo);
        list.Controls.Add(new Label { Text = "Kinetik", Font = Theme.UI(16f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = false, Size = new Size(RowW, 36), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 0, 0) });
        Hint("A lightweight stats overlay for the Windows taskbar.", 28);
        Add(Icons.Info, Theme.Accent, "Version", null, new Label { Text = VersionText, AutoSize = false, Size = new Size(120, 24), TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Text, BackColor = Theme.Card, Font = Theme.UI(10f) });
        Button upd = null;
        Updates.Release newer = null;
        upd = Btn("Check for updates", (s, e) =>
        {
            if (newer != null)
            {
                if (newer.ZipUrl == null || newer.ExeSha == null) { Updates.OpenUrl(newer.Url); return; } // older releases: by hand
                if (MessageBox.Show(this, "Download Kinetik " + newer.Name + " from GitHub and install it?\n\nKinetik checks the download against the checksum in the release notes, replaces its files and restarts.",
                    "Kinetik", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                upd.Text = "Installing…"; upd.Enabled = false;
                Updates.Install(newer, err =>
                {
                    if (upd.IsDisposed) return;
                    upd.Enabled = true; upd.Text = "Install " + newer.Name;
                    MessageBox.Show(this, "The update wasn't installed.\n\n" + err, "Kinetik", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }, this);
                return;
            }
            upd.Text = "Checking…"; upd.Enabled = false;
            Updates.Check(c.UpdateBeta, (latest, error) =>
            {
                if (upd.IsDisposed) return;
                upd.Enabled = true;
                if (error != null) upd.Text = "Couldn't check. Try again";
                else if (Updates.IsNewer(latest)) { newer = latest; upd.Text = latest.ZipUrl != null && latest.ExeSha != null ? "Install " + latest.Name : "Get version " + latest.Name; }
                else upd.Text = "Up to date ✓";
            }, this);
        });
        Add(Icons.Down, Theme.Accent, "Updates", "Looks for a newer version on GitHub, and can install it for you", upd);
        Switch(Icons.Star, Theme.Accent, "Include test versions", "UpdateBeta", "Offer early builds marked as test versions on GitHub");
        Add(Icons.Heart, Theme.Accent, "© Akila Sella Hennedige", "Free software under the GNU General Public License v3", null);
    }

    // ---- Widget: a desktop panel with its own stats, order and look.
    void BuildWidget()
    {
        Hint("A desktop panel with live bars and graphs. Drag it to move it and right-click it for quick options. Icons and colours are shared with the taskbar bar.", 48);

        Header("Desktop widget");
        Switch(Icons.Monitor, Theme.Accent, "Show desktop widget", "WidgetShow");
        Add(Icons.Layers, Theme.Accent, "Placement", "Where it sits among your windows",
            Combo(new[] { "On the desktop (behind windows)", "Normal window", "Always on top" }, c.WidgetLayer, i => { c.WidgetLayer = i; Apply(); }, 230));
        Switch(Icons.Grip, Theme.Accent, "Lock position", "WidgetLocked", "Stops it being dragged by accident");
        Switch(Icons.Mouse, Theme.Accent, "Click-through when locked", "WidgetClickThrough", "Clicks pass to whatever is underneath. Unlock it from the taskbar bar's menu.");
        Switch(Icons.Spacing, Theme.Accent, "Snap to screen edges", "WidgetSnap");
        Add(Icons.Monitor, Theme.Accent, "Position", "Moves it back to the top-right corner", Btn("Reset position", (s, e) => { c.WidgetX = c.WidgetY = int.MinValue; Apply(); }));

        Header("Look");
        Add(Icons.Pulse, Theme.Accent, "Style", null,
            Combo(new[] { "Bars", "Graphs", "Bars and graphs", "Minimal" }, c.WidgetStyle, i => { c.WidgetStyle = i; Apply(); }, 170));
        Add(Icons.Palette, Theme.Accent, "Theme", null,
            Combo(new[] { "Dark", "Light", "Match Windows" }, c.WidgetTheme, i => { c.WidgetTheme = i; Apply(); }, 170));
        var bgTools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty };
        var sw = new Swatch { Value = c.WidgetBg.A != 0 ? Color.FromArgb(255, c.WidgetBg) : Color.FromArgb(22, 22, 26), Margin = new Padding(0, 3, 8, 0) };
        sw.Click += (s, e) =>
        {
            using (var dlg = new ColorDialog { Color = sw.Value, FullOpen = true })
                if (dlg.ShowDialog(this) == DialogResult.OK) { c.WidgetBg = Color.FromArgb(255, dlg.Color); Apply(); RefreshPage(); }
        };
        bgTools.Controls.Add(sw);
        bgTools.Controls.Add(Btn("Default", (s, e) => { c.WidgetBg = Color.FromArgb(0, 0, 0, 0); Apply(); RefreshPage(); }));
        Add(Icons.Palette, Theme.Accent, "Background colour", c.WidgetBg.A != 0 ? "Custom colour" : "Follows the theme", bgTools);
        SliderRow(Icons.Palette, Theme.Accent, "Background opacity", "WidgetOpacity", 0, 100, "%");
        SliderRow(Icons.Chip, Theme.Accent, "Size", "WidgetScale", 60, 180, "%");
        SliderRow(Icons.Spacing, Theme.Accent, "Width", "WidgetWidth", 200, 520, " px");
        SliderRow(Icons.Palette, Theme.Accent, "Corner roundness", "WidgetRound", 0, 200, "%");
        Switch(Icons.Layers, Theme.Accent, "Shadow", "WidgetShadow");
        Switch(Icons.Fan, Theme.Accent, "Animated icons", "WidgetAnimate", "Icons move with activity: fans spin, arrows flow, temps flicker");
        var rates = new[] { 15, 30, 60 };
        Add(Icons.Gauge, Theme.Accent, "Animation frame rate", "Smoother looks nicer; lower uses less CPU. Shared with the widget.",
            Combo(new[] { "15 fps (lightest)", "30 fps", "60 fps (smoothest)" }, Math.Max(0, Array.IndexOf(rates, c.AnimFps)), i => { c.AnimFps = rates[i]; Apply(); }, 170));
        var title = new TextBox { Text = c.WidgetTitle, Width = 170, BackColor = Theme.CardHover, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI(9.5f) };
        Action commit = () => { var v = title.Text.Trim(); if (v != c.WidgetTitle) { c.WidgetTitle = v; Apply(); } };
        title.Leave += (s, e) => commit();
        title.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { commit(); e.SuppressKeyPress = true; } };
        Add(Icons.Text, Theme.Accent, "Title", "Shown at the top. Leave empty for none.", title);
        Switch(Icons.List, Theme.Accent, "Group headings", "WidgetHeadings", "Processor, Graphics, Memory… above each group");
        Switch(Icons.Chip, Theme.Accent, "Hardware names", "WidgetHwNames", "CPU and GPU model next to the headings");
        var secs = new[] { 30, 60, 120, 300 };
        Add(Icons.Clock, Theme.Accent, "Graph history", null,
            Combo(new[] { "30 seconds", "1 minute", "2 minutes", "5 minutes" }, Array.IndexOf(secs, c.WidgetGraphSecs), i => { c.WidgetGraphSecs = secs[i]; Apply(); }, 150));

        Header("Contents");
        Hint("Switch on the stats to show, and drag rows to reorder them. Dividers add a line within a group.", 30);
        var tools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, 8) };
        var addBtn = Btn("+  Add divider", (s, e) =>
        {
            var id = c.NewDividerId();
            var o = c.WidgetOrderList();
            int at = arrangeList != null && arrangeList.SelectedId != null ? o.IndexOf(arrangeList.SelectedId) + 1 : o.Count;
            o.Insert(at, id);
            c.WidgetOrder = string.Join(",", o);
            Apply();
            pendingSelect = id;
            RefreshPage();
        }, true);
        addBtn.Margin = new Padding(0, 0, 8, 0);
        var resetBtn = Btn("Reset contents", (s, e) =>
        {
            if (MessageBox.Show(this, "Put the widget's stats back to the default selection and order, and remove its dividers?", "Kinetik", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            c.WidgetOrder = Settings.DefaultWidgetOrder;
            c.WidgetItems = Settings.DefaultWidgetItems;
            Apply(); RefreshPage();
        });
        tools.Controls.Add(addBtn); tools.Controls.Add(resetBtn);
        list.Controls.Add(tools);

        arrangeList = new ReorderList(c, c.WidgetOrderList(), RowW, c.WidgetIsOn, c.SetWidgetOn, "Separator line") { Margin = new Padding(0, 4, 0, 0) };
        arrangeList.Moved += order => { c.WidgetOrder = string.Join(",", order); Apply(); };
        arrangeList.Toggled += id => Apply();
        arrangeList.Removed += id =>
        {
            c.WidgetOrder = string.Join(",", c.WidgetOrderList().Where(x => x != id));
            c.SetCustom(id, "", null);
            Apply();
        };
        arrangeList.IconClicked += ShowIconPicker;
        list.Controls.Add(arrangeList);
    }

    // ---- Game overlay: an always-on-top strip with its own stats and order, for use while gaming.
    void BuildOverlay()
    {
        Hint("A slim strip that stays on top of games so you can watch your stats while you play. It shows over games in borderless or windowed mode; exclusive fullscreen hides it.", 48);

        Header("Game overlay");
        Switch(Icons.Gamepad, Theme.Accent, "Show game overlay", "OverlayShow");
        Switch(Icons.Gamepad, Theme.Accent, "Show automatically in games", "OverlayAuto", "Appears while a fullscreen or borderless game is in front, and hides when you leave it. Needs admin.");
        Switch(Icons.Bolt, Theme.Accent, "Hotkey: Ctrl+Shift+F10", "OverlayHotkey", "Shows or hides the overlay from inside a game");
        Switch(Icons.Mouse, Theme.Accent, "Lock position (click-through)", "OverlayLocked", "Clicks go to the game. Unlock it from the taskbar bar's menu.");
        Add(Icons.Monitor, Theme.Accent, "Position", "Drag it while unlocked. This moves it back to the top-left corner.", Btn("Reset position", (s, e) => { c.OverlayX = c.OverlayY = int.MinValue; Apply(); }));
        if (!Program.IsAdmin)
            Add(Icons.Monitor, c.GpuColor, "FPS counter needs admin", "Frame rates are read from Windows' graphics events", Btn("Restart as admin", (s, e) => Program.RestartAsAdmin(), true));

        Header("Sessions and benchmarks");
        Switch(Icons.List, Theme.Accent, "Session summary", "SessionSummary", "When a game closes: play time, average FPS, 1% low and peak temperatures, saved to a log. Needs admin.");
        Add(Icons.List, Theme.Accent, "Session log", "Sessions.csv, one line per game session", Btn("Open folder", (s, e) => { Directory.CreateDirectory(GameWatch.DataDir); GameWatch.OpenFolder(GameWatch.DataDir); }));
        Switch(Icons.Bolt, Theme.Accent, "Hotkey: Ctrl+Shift+F11", "BenchHotkey", "Starts and stops recording a benchmark: every stat, each update, to a CSV file");
        var benchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kinetik Benchmarks");
        Add(Icons.Pulse, Theme.Accent, "Benchmarks", "Saved in Documents\\Kinetik Benchmarks", Btn("Open folder", (s, e) => { Directory.CreateDirectory(benchDir); GameWatch.OpenFolder(benchDir); }));

        Header("Look");
        Add(Icons.Text, Theme.Accent, "Labels", null,
            Combo(new[] { "Text (CPU, GPU, FPS…)", "Icons" }, c.OverlayText ? 0 : 1, i => { c.OverlayText = i == 0; Apply(); }, 190));
        SliderRow(Icons.Chip, Theme.Accent, "Size", "OverlayScale", 60, 250, "%");
        SliderRow(Icons.Palette, Theme.Accent, "Background opacity", "OverlayOpacity", 0, 100, "%");

        Header("Contents");
        Hint("Switch on the stats to show, and drag rows to reorder them (left → right). Icons and colours are shared with the taskbar bar.", 30);
        var tools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, 8) };
        var addBtn = Btn("+  Add divider", (s, e) =>
        {
            var id = c.NewDividerId();
            var o = c.OverlayOrderList();
            int at = arrangeList != null && arrangeList.SelectedId != null ? o.IndexOf(arrangeList.SelectedId) + 1 : o.Count;
            o.Insert(at, id);
            c.OverlayOrder = string.Join(",", o);
            Apply();
            pendingSelect = id;
            RefreshPage();
        }, true);
        addBtn.Margin = new Padding(0, 0, 8, 0);
        var resetBtn = Btn("Reset contents", (s, e) =>
        {
            if (MessageBox.Show(this, "Put the overlay's stats back to the default selection and order, and remove its dividers?", "Kinetik", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            c.OverlayOrder = Settings.DefaultOverlayOrder;
            c.OverlayItems = Settings.DefaultOverlayItems;
            Apply(); RefreshPage();
        });
        tools.Controls.Add(addBtn); tools.Controls.Add(resetBtn);
        list.Controls.Add(tools);

        arrangeList = new ReorderList(c, c.OverlayOrderList(), RowW, c.OverlayIsOn, c.SetOverlayOn, "Separator line") { Margin = new Padding(0, 4, 0, 0) };
        arrangeList.Moved += order => { c.OverlayOrder = string.Join(",", order); Apply(); };
        arrangeList.Toggled += id => Apply();
        arrangeList.Removed += id =>
        {
            c.OverlayOrder = string.Join(",", c.OverlayOrderList().Where(x => x != id));
            c.SetCustom(id, "", null);
            Apply();
        };
        arrangeList.IconClicked += ShowIconPicker;
        list.Controls.Add(arrangeList);
    }

    // Applies settings loaded from a profile or backup. The window's colours are fixed when it's built, so it reopens.
    void LoadSettings(Func<bool> load)
    {
        try { if (!load()) return; }
        catch (Exception ex) { MessageBox.Show(this, "Couldn't load those settings.\n\n" + ex.Message, "Kinetik", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Theme.Use(c.AppBackground);
        bar.Render(true);
        var b = bar; var p = page;
        BeginInvoke((Action)(() => { Close(); b.OpenSettings(p); }));
    }

    void BuildGeneral()
    {
        Header("Settings window");
        Add(Icons.Palette, Theme.Accent, "Background colour", "The colour of this settings window",
            Combo(Theme.Backgrounds.Select(x => x.Key).ToArray(), Math.Max(0, Math.Min(Theme.Backgrounds.Length - 1, c.AppBackground)), i =>
            {
                if (i == c.AppBackground) return;
                c.AppBackground = i; c.Save(); Theme.Use(i);
                // Controls take their colours when they're built, so reopen the window on this page.
                BeginInvoke((Action)(() => { var b = bar; Close(); b.OpenSettings("General"); }));
            }, 170));

        Header("Behaviour");
        var intervals = new[] { 500, 1000, 2000, 3000, 5000 };
        Add(Icons.Gauge, Theme.Accent, "Update interval", "Slower updates use less CPU",
            Combo(intervals.Select(i => (i / 1000.0) + " s").ToArray(), Array.IndexOf(intervals, c.Interval), i => { c.Interval = intervals[i]; Apply(); }, 110));
        var off = new NumericUpDown { Minimum = 0, Maximum = 3000, Increment = 10, Value = Math.Max(0, Math.Min(3000, c.Offset)), Width = 90, BackColor = Theme.CardHover, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        off.ValueChanged += (s, e) => { c.Offset = (int)off.Value; Apply(); };
        Add(Icons.Spacing, Theme.Accent, "Shift left", "Pixels between the bar and the tray icons", off);
        Switch(Icons.Chip, Theme.Accent, "Left-click opens Task Manager", "ClickTaskMgr");
        Switch(Icons.Monitor, Theme.Accent, "Hide in fullscreen apps", "HideFullscreen", "Games, videos and presentations");
        Switch(Icons.Monitor, Theme.Accent, "Show on all taskbars", "AllTaskbars", "A copy of the bar on every monitor's taskbar, left of its clock");
        Switch(Icons.Grid, Theme.Accent, "Show tray icon", "TrayIcon", "Click it for Settings, right-click for the menu");
        Switch(Icons.Fan, Theme.Accent, "Spinning icon", "TrayAnimate", "The fan logo in the tray and taskbar spins faster the busier your CPU is");
        Switch(Icons.Monitor, Theme.Accent, "Show on the taskbar", "TaskbarButton", "A taskbar button like other running apps. Click it for Settings.");
        var lnk = new Toggle { On = File.Exists(Shortcuts.DesktopPath) };
        lnk.Changed += (s, e) => { if (lnk.On) Shortcuts.CreateDesktop(c.TrayStyle); else Shortcuts.RemoveDesktop(); lnk.On = File.Exists(Shortcuts.DesktopPath); };
        Add(Icons.Star, Theme.Accent, "Desktop shortcut", "Opens Kinetik, or its Settings if it's already running", lnk);
        Add(Icons.Palette, Theme.Accent, "Icon background", "For the tray icon and the taskbar button",
            Combo(TrayIconArt.Styles.Select(x => x.Key).ToArray(), Math.Max(0, Math.Min(TrayIconArt.Styles.Length - 1, c.TrayStyle)), i =>
            {
                c.TrayStyle = i; Apply();
                Icon = TrayIconArt.Frame(i, SystemInformation.IconSize.Width, 0);
                if (File.Exists(Shortcuts.DesktopPath)) Shortcuts.CreateDesktop(i); // so the shortcut's icon matches too
            }, 170));

        Header("Warnings");
        Hint("Values switch to the warning colour past these limits.", 30);
        SliderRow(Icons.Chip, c.CpuColor, "CPU usage above", "WarnCpu", 50, 100, "%");
        SliderRow(Icons.Ram, c.RamColor, "RAM / VRAM usage above", "WarnRam", 50, 100, "%");
        SliderRow(Icons.Gpu, c.GpuColor, "GPU usage above", "WarnGpu", 50, 100, "%");
        SliderRow(Icons.Thermo, c.TempColor, "CPU temperature above", "WarnCpuTemp", 50, 110, "°C");
        SliderRow(Icons.Flame, c.GpuColor, "GPU temperature above", "WarnGpuTemp", 50, 110, "°C");
        SliderRow((g, r, col) => Icons.Battery(g, r, col, 20), c.BatColor, "Battery below", "WarnBattery", 5, 50, "%");
        SliderRow(Icons.Signal, c.UpColor, "Ping above", "WarnPing", 20, 500, " ms");
        SliderRow(Icons.Pie, c.DiskColor, "Free disk space below", "WarnFreePct", 1, 50, "%");

        Header("Notifications");
        Switch(Icons.Thermo, c.TempColor, "Hot CPU or GPU", "AlertTemps", "When either stays above its warning limit for 30 seconds");
        Switch((g, r, col) => Icons.Battery(g, r, col, 20), c.BatColor, "Low batteries", "AlertBattery", "When a Bluetooth device or the laptop drops below its warning limit");
        Switch(Icons.Pie, c.DiskColor, "Drive almost full", "AlertDisk", "When the free-space drive drops below its warning limit (at most every 6 hours)");
        Switch(Icons.Signal, c.UpColor, "High ping", "AlertPing", "When ping stays above its warning limit or times out for 30 seconds. Pings even while ping isn't shown.");
        Add(Icons.Info, Theme.Accent, "Test", "Notifications use Windows' own, so Focus assist and Do not disturb apply", Btn("Send a test", (s, e) => bar.Notify("Kinetik", "Notifications are working.", null)));

        Header("Profiles and backup");
        var names = Profiles.List();
        var pick = Combo(names.Count > 0 ? names.ToArray() : new[] { "(no profiles yet)" }, 0, i => { }, 170);
        pick.Enabled = names.Count > 0;
        var profTools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty };
        pick.Margin = new Padding(0, 3, 8, 0);
        profTools.Controls.Add(pick);
        profTools.Controls.Add(Btn("Load", (s, e) => { if (names.Count > 0) LoadSettings(() => Profiles.Load(c, names[pick.SelectedIndex])); }));
        var del = Btn("Delete", (s, e) =>
        {
            if (names.Count == 0 || MessageBox.Show(this, "Delete the profile \"" + names[pick.SelectedIndex] + "\"?", "Kinetik", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Profiles.Delete(names[pick.SelectedIndex]); RefreshPage();
        });
        del.Margin = new Padding(6, 0, 0, 0);
        profTools.Controls.Add(del);
        Add(Icons.Layers, Theme.Accent, "Profiles", "Switch between saved setups, e.g. Gaming and Work. Also in the right-click menu.", profTools);
        var newName = new TextBox { Width = 150, BackColor = Theme.CardHover, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI(9.5f) };
        var saveTools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty };
        newName.Margin = new Padding(0, 4, 8, 0);
        saveTools.Controls.Add(newName);
        saveTools.Controls.Add(Btn("Save", (s, e) =>
        {
            var n = Profiles.Clean(newName.Text);
            if (n == "") { newName.Focus(); return; }
            try { c.Save(); Profiles.Save(c, n); RefreshPage(); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save the profile.\n\n" + ex.Message, "Kinetik", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }, true));
        Add(Icons.Star, Theme.Accent, "Save current settings as a profile", "Give it a name", saveTools);
        var backupTools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty };
        backupTools.Controls.Add(Btn("Back up…", (s, e) =>
        {
            using (var dlg = new SaveFileDialog { Filter = "Kinetik settings (*.kinetik)|*.kinetik", FileName = "Kinetik settings.kinetik" })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    try { c.Save(); File.WriteAllText(dlg.FileName, Profiles.Export(c)); }
                    catch (Exception ex) { MessageBox.Show(this, "Couldn't save the backup.\n\n" + ex.Message, "Kinetik", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }));
        var restore = Btn("Restore…", (s, e) =>
        {
            using (var dlg = new OpenFileDialog { Filter = "Kinetik settings (*.kinetik)|*.kinetik" })
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadSettings(() => { Profiles.Import(c, File.ReadAllText(dlg.FileName)); c.Save(); return true; });
        });
        restore.Margin = new Padding(6, 0, 0, 0);
        backupTools.Controls.Add(restore);
        Add(Icons.Gear, Theme.Accent, "Backup", "Save every setting to a file, or restore them from one", backupTools);

        Header("Startup & permissions");
        var startup = new Toggle { On = Program.IsStartupEnabled() };
        startup.Changed += (s, e) => { Program.SetStartup(startup.On); startup.On = Program.IsStartupEnabled(); };
        Add(Icons.Bolt, Theme.Accent, "Run at startup", Program.IsAdmin ? "Starts with admin rights at login (no UAC prompt)" : "Restart as admin first to enable CPU temp at startup", startup);
        if (!Program.IsAdmin)
            Add(Icons.Thermo, c.TempColor, "Run as administrator", "Required for an exact CPU temperature", Btn("Restart as admin", (s, e) => Program.RestartAsAdmin(), true));
        else if (!Program.PawnIOInstalled)
            Add(Icons.Thermo, c.TempColor, "Install the PawnIO driver", "Required for an exact CPU temperature. Restart Kinetik after installing.", Btn("Get PawnIO", (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://pawnio.eu") { UseShellExecute = true }); } catch { }
            }, true));
        else
            Add(Icons.Thermo, c.TempColor, "Running as administrator", "CPU temperature and power are available", null);

        Header("Reset");
        Add(Icons.Gear, Theme.Sub, "Restore default settings", "Every option, including the order and custom icons", Btn("Reset", (s, e) =>
        {
            if (MessageBox.Show(this, "Reset every setting to its default?", "Kinetik", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            c.ResetToDefaults(); Apply(); RefreshPage();
        }));
    }
}
