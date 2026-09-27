// PC Stats Bar: a TrafficMonitor-style overlay that sits on the taskbar, left of the tray.
// Shows network speed and ping, CPU (usage, clock, temp, power, per-core), RAM and commit, GPU (usage, temp, clock,
// fan, VRAM, power), disk activity / throughput / free space, processes, uptime, and battery levels of the PC and
// connected Bluetooth devices. Every item can be reordered, given its own icon and colour, and split into groups
// with dividers. CPU temperature/power come from LibreHardwareMonitorLib (embedded in the exe) and need admin rights.
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

[assembly: AssemblyTitle("PC Stats Bar")]
[assembly: AssemblyDescription("Taskbar overlay showing PC stats and device battery levels")]
[assembly: AssemblyProduct("PC Stats Bar")]
[assembly: AssemblyCompany("Akila Sella Hennedige")]
[assembly: AssemblyCopyright("Copyright © 2026 Akila Sella Hennedige. GNU GPL v3.")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]
[assembly: AssemblyInformationalVersion("1.4.0")]

// ======================================================================= Settings
class Settings
{
    const string Key = @"Software\PCStatsBar";
    public const int CurrentVersion = 2;
    public int SettingsVersion = CurrentVersion;

    // What to show. Each id in StatIds is also the name of its on/off field.
    public bool Up = true, Down = true, NetTotal = false, Ping = false;
    public bool Cpu = true, CpuCores = false, CpuClock = true, CpuTemp = true, CpuPower = false;
    public bool Ram = true, RamGb = true, RamCommit = false;
    public bool Gpu = true, GpuTemp = true, GpuClock = false, GpuFan = false, GpuVram = false, GpuVramPct = false, GpuPower = false;
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
    public bool WidgetLocked = false, WidgetClickThrough = false, WidgetSnap = true, WidgetHeadings = true, WidgetHwNames = true, WidgetShadow = true;
    public string WidgetTitle = "";
    public Color WidgetBg = Color.FromArgb(0, 0, 0, 0); // fully transparent = the theme's own background colour

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
    public bool ClickTaskMgr = true, HideFullscreen = true;

    // Every stat that can appear on the bar, in default order. Dividers ("Sep1", "Sep2", ...) are added by the user.
    public const string DefaultOrder = "Up,Down,NetTotal,Ping,Cpu,CpuCores,CpuClock,CpuTemp,CpuPower,Ram,RamGb,RamCommit," +
        "Gpu,GpuTemp,GpuClock,GpuFan,GpuVram,GpuVramPct,GpuPower,Disk,DiskRead,DiskWrite,DiskFree,Processes,Uptime,PcBattery,BatTime,Batteries";
    public static readonly string[] StatIds = DefaultOrder.Split(',');

    // The widget has its own order (grouped by hardware) and its own set of stats that are switched on.
    public const string DefaultWidgetOrder = "Cpu,CpuTemp,CpuClock,CpuPower,CpuCores,Gpu,GpuTemp,GpuClock,GpuFan,GpuVram,GpuVramPct,GpuPower," +
        "Ram,RamGb,RamCommit,Down,Up,NetTotal,Ping,Disk,DiskRead,DiskWrite,DiskFree,Processes,Uptime,PcBattery,BatTime,Batteries";
    public const string DefaultWidgetItems = "Cpu,CpuTemp,CpuClock,Gpu,GpuTemp,GpuVram,Ram,RamGb,Down,Up";
    public static readonly string[] WidgetIds = DefaultWidgetOrder.Split(',');

    public static bool IsDivider(string id) { return id.Length > 3 && id.StartsWith("Sep") && id.Substring(3).All(char.IsDigit); }

    public List<string> OrderList() { return Merge(Order, StatIds); }
    public List<string> WidgetOrderList() { return Merge(WidgetOrder, WidgetIds); }

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
        var used = OrderList().Concat(WidgetOrderList()).ToList(); // unique across both, so per-divider colours don't clash
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

    Tuple<string, HashSet<string>> widgetCache; // read from the sampler thread too, so it's swapped as one object
    public bool WidgetIsOn(string id)
    {
        if (IsDivider(id)) return true;
        var src = WidgetItems ?? "";
        var cache = widgetCache;
        if (cache == null || cache.Item1 != src) widgetCache = cache = Tuple.Create(src, new HashSet<string>(src.Split(',')));
        return cache.Item2.Contains(id);
    }

    public void SetWidgetOn(string id, bool on)
    {
        var set = (WidgetItems ?? "").Split(',').Where(x => x != "" && x != id).ToList();
        if (on) set.Add(id);
        WidgetItems = string.Join(",", set);
    }

    // True when the bar or the widget shows this stat, i.e. when it needs measuring.
    public bool Wants(string id) { return IsOn(id) || (WidgetShow && WidgetIsOn(id)); }

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
    public int Processes = -1, Ping = -1; // Ping: -1 = no reply yet, -2 = timed out
    public double Uptime;
    public string CpuName = "", CpuTempStatus = "";
}

// Reads sensors through LibreHardwareMonitorLib: CPU temperature/power (needs admin + PawnIO driver)
// and AMD / Intel GPUs (no admin needed). NVIDIA GPUs are read directly through the driver's nvml.dll.
// Kept in its own class so the library is only loaded once it is actually needed.
class HwSensors
{
    LibreHardwareMonitor.Hardware.Computer computer;
    LibreHardwareMonitor.Hardware.IHardware cpu;
    List<LibreHardwareMonitor.Hardware.IHardware> gpus = new List<LibreHardwareMonitor.Hardware.IHardware>();
    string bestGpu;
    public string Error = "";
    public bool CpuEnabled;

    public bool HasCpu { get { return cpu != null; } }
    public List<string> GpuNames { get { return gpus.Select(g => g.Name).ToList(); } }

    public bool Open(bool withCpu)
    {
        CpuEnabled = withCpu;
        try
        {
            computer = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = withCpu, IsGpuEnabled = true };
            computer.Open();
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
        computer = null; cpu = null; gpus = new List<LibreHardwareMonitor.Hardware.IHardware>();
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

    // NVIDIA driver's management library (ships with the driver in System32).
    [StructLayout(LayoutKind.Sequential)] struct NvUtil { public uint Gpu, Mem; }
    [StructLayout(LayoutKind.Sequential)] struct NvMem { public ulong Total, Free, Used; }
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
    List<PerformanceCounter> cores;
    double maxMhz;
    string cpuName = "";
    IntPtr gpu = IntPtr.Zero; string gpuName = "";

    NetworkInterface[] nics; string nicKey = ""; DateTime nicsAt;
    long lastRx, lastTx; DateTime lastNet = DateTime.MinValue;

    HwSensors sensors; string sensorStatus = ""; bool sensorsFailed;
    DateTime sensorsIdleSince = DateTime.MaxValue;
    volatile bool gpuScanRequested;
    volatile List<string> lhmGpus = new List<string>();

    double freeGb = -1, freePct; string freeName = "", freeFor; DateTime freeAt;
    System.Threading.Timer pingTimer, batTimer;
    volatile int pingMs = -1;
    int pinging, readingBatteries;

    public Sampler(Settings cfg, Action onSample) { this.cfg = cfg; this.onSample = onSample; }

    public void Start()
    {
        new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        NetworkChange.NetworkAddressChanged += (s, e) => nics = null;
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
            if (nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out gpu) == 0)
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

        if (!Program.IsAdmin) sensorStatus = "needs admin – right-click → Restart as administrator";
        else if (sensors != null && sensors.HasCpu && (c.Wants("CpuTemp") || c.Wants("CpuPower")))
        {
            try
            {
                sensors.ReadCpu(out s.CpuTemp, out s.CpuPower);
                sensorStatus = double.IsNaN(s.CpuTemp) ? "no temperature sensor reported (is the PawnIO driver installed?)" : "";
            }
            catch (Exception e) { sensorStatus = "read failed (" + e.Message + ")"; }
        }
        s.CpuTempStatus = sensorStatus;

        if (c.Wants("DiskFree")) { ReadFree(now); s.FreeGb = freeGb; s.FreePct = freePct; s.FreeName = freeName; }
        ManagePing();
        s.Ping = c.Wants("Ping") ? pingMs : -1;
        ManageBatteries();
        s.Uptime = GetTickCount64() / 1000.0;
        return s;
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
        bool wantGpu = (AnyGpuStat && !NvidiaChosen) || gpuScanRequested;
        if (wantCpu || wantGpu)
        {
            sensorsIdleSince = DateTime.MaxValue;
            if (sensors != null && wantCpu && !sensors.CpuEnabled) CloseSensors();
            if (sensors == null && !sensorsFailed) OpenSensors(wantCpu);
            gpuScanRequested = false;
        }
        else if (sensors != null)
        {
            if (sensorsIdleSince == DateTime.MaxValue) sensorsIdleSince = now;
            else if ((now - sensorsIdleSince).TotalSeconds > 60) { CloseSensors(); Program.TrimMemory(); }
        }
    }

    void OpenSensors(bool withCpu)
    {
        try
        {
            var hw = new HwSensors();
            if (!hw.Open(withCpu)) { sensorStatus = "unavailable (" + hw.Error + ")"; sensorsFailed = true; return; }
            sensorStatus = hw.Error != "" ? "unavailable (" + hw.Error + ")" : "";
            sensors = hw;
            lhmGpus = hw.GpuNames;
        }
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
    public const string AppName = "PCStatsBar";
    public static readonly bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>();

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

    [STAThread]
    static void Main(string[] args)
    {
        // No background GC thread: this app's heap is tiny, so concurrent collection only costs memory.
        GCSettings.LatencyMode = GCLatencyMode.Batch;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
        bool created;
        using (var mutex = new Mutex(true, AppName, out created))
        {
            // When relaunching (e.g. elevated), wait for the previous instance to exit.
            if (!created && !(args.Contains("--restart") && Wait(mutex))) return;
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "PCStatsBar.log"), DateTime.Now + " " + e.Exception + Environment.NewLine);
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

    // The LibreHardwareMonitor DLLs are embedded as resources so the app stays a single exe.
    static Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name;
        lock (loaded)
        {
            Assembly a;
            if (loaded.TryGetValue(name, out a)) return a;
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name + ".dll"))
            {
                if (st != null)
                {
                    var bytes = new byte[st.Length];
                    st.Read(bytes, 0, bytes.Length);
                    a = Assembly.Load(bytes);
                }
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

    // ---- Startup: an elevated logon task when admin (no UAC prompt at login), otherwise the Run key.
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static int Run(string exe, string args)
    {
        try
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }))
            {
                p.WaitForExit(15000);
                return p.ExitCode;
            }
        }
        catch { return -1; }
    }

    public static bool TaskExists() { return Run("schtasks.exe", "/Query /TN " + AppName) == 0; }

    public static bool IsStartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            if (k != null && k.GetValue(AppName) != null) return true;
        return TaskExists();
    }

    public static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue(AppName, false);
        if (IsAdmin) Run("schtasks.exe", "/Delete /F /TN " + AppName);
        if (!on) return;
        if (IsAdmin && CreateLogonTask(Application.ExecutablePath, AppName, true)) return;
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
    }

    // Registers a logon task with schtasks.exe. It uses a task XML file because plain schtasks switches can't turn off
    // "only start on AC power" or the 72-hour run limit, which would stop the bar on laptops or after three days.
    // (This used to go through PowerShell, which some antivirus programs flag.)
    public static bool CreateLogonTask(string exe, string name, bool elevated)
    {
        var user = System.Security.SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo><Description>Starts PC Stats Bar when you sign in.</Description></RegistrationInfo>\r\n" +
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
        var file = Path.Combine(Path.GetTempPath(), name + "-task.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return Run("schtasks.exe", "/Create /F /TN \"" + name + "\" /XML \"" + file + "\"") == 0;
        }
        catch { return false; }
        finally { try { File.Delete(file); } catch { } }
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
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    public readonly Settings Cfg = new Settings();
    public readonly Sampler Sampler;
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

        menu.Renderer = new DarkMenuRenderer();
        menu.Items.Add("Settings…", null, (s, e) => OpenSettings(null));
        menu.Items.Add("Arrange stats…", null, (s, e) => OpenSettings("Arrange"));
        var widgetItem = new ToolStripMenuItem("Desktop widget");
        widgetItem.Click += (s, e) => SetWidget(!Cfg.WidgetShow);
        var unlockItem = new ToolStripMenuItem("Unlock desktop widget");
        unlockItem.Click += (s, e) => { Cfg.WidgetLocked = false; Cfg.Save(); Render(true); NotifySettings("Widget"); };
        menu.Items.Add(widgetItem);
        menu.Items.Add(unlockItem);
        menu.Opening += (s, e) => { widgetItem.Checked = Cfg.WidgetShow; unlockItem.Visible = Cfg.WidgetShow && Cfg.WidgetLocked; };
        menu.Items.Add("Refresh batteries now", null, (s, e) => ThreadPool.QueueUserWorkItem(_ => Sampler.ReadBatteries()));
        if (!Program.IsAdmin) menu.Items.Add("Restart as administrator (CPU temp)", null, (s, e) => Program.RestartAsAdmin());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Close());

        Sampler = new Sampler(Cfg, () => { try { BeginInvoke((Action)Render); } catch { } });
        HandleCreated += (s, e) => { Sampler.Start(); TrimSoon(15000); };
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
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        surface.Dispose();
        if (disposing && widget != null) widget.Dispose();
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

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        else if (e.Button == MouseButtons.Left && Cfg.ClickTaskMgr)
            try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { }
        base.OnMouseUp(e);
    }

    // ---- Layout model ----
    class Part { public string Text, Template; public Color Color; public bool Bold; public float W; }
    class Seg
    {
        public IconFn Icon; public string Label; public Color IconColor; public float LabelW;
        public List<Part> Parts = new List<Part>();
        public double[] Bars; public Color BarColor;
        public bool IsSep; public float Mark; // divider: size of the line / dot
        public float W;
    }
    class BarLayout
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

    List<Seg> BuildSegments(Snapshot s, Color val, Color dim)
    {
        var c = Cfg;
        var segs = new List<Seg>();
        Func<double, int, Color> warn = (v, limit) => v >= limit ? c.WarnColor : val;

        foreach (var id in c.OrderList())
        {
            if (!c.IsOn(id)) continue;
            var info = Stats.Get(id, c);
            bool customIcon = c.IconOf(id) != "";
            Func<Seg> seg = () => new Seg { Icon = info.Icon, Label = info.TextLabel ? info.Short : null, IconColor = info.Color };
            Action<string, string, Color> add = (text, template, color) =>
            {
                var sg = seg();
                sg.Parts.Add(new Part { Text = text, Template = template, Color = color, Bold = color != dim });
                segs.Add(sg);
            };
            switch (id)
            {
                case "Up": add(Speed(s.Up), "88.8 MB/s", val); break;
                case "Down": add(Speed(s.Down), "88.8 MB/s", val); break;
                case "NetTotal": add(Speed(s.Up + s.Down), "88.8 MB/s", val); break;
                case "Ping":
                    add(s.Ping >= 0 ? s.Ping + " ms" : "-- ms", "888 ms", s.Ping == -2 || s.Ping >= c.WarnPing ? c.WarnColor : s.Ping < 0 ? dim : val);
                    break;
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
    BarLayout Build(int height, bool light)
    {
        Color val = Cfg.ValueColor, dim = Cfg.LabelColor;
        if (Cfg.AutoTheme)
        {
            val = light ? Color.FromArgb(20, 20, 20) : Color.White;
            dim = light ? Color.FromArgb(95, 95, 100) : Color.FromArgb(150, 150, 160);
        }
        var segs = BuildSegments(Sampler.Snap, val, dim);
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

    void PaintBar(Graphics g, BarLayout L, int height)
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
                float sx = cx;
                if (seg.Label != null)
                {
                    brush.Color = seg.IconColor;
                    g.DrawString(seg.Label, bold, brush, sx, cy + (rowH - L.LineH) / 2, Fmt);
                    sx += seg.LabelW + L.IconGap;
                }
                else if (seg.Icon != null)
                {
                    seg.Icon(g, new RectangleF(sx, cy + (rowH - icon) / 2, icon, icon), seg.IconColor);
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
        SyncWidget();
        if (widget != null) { if (fullscreen) widget.HideNow(); else widget.Render(force); }

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        RECT tb;
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out tb)) return;
        if (fullscreen) { ShowWindow(Handle, 0); return; }

        TaskbarHeight = tb.B - tb.T;
        var L = Build(TaskbarHeight, LightTaskbar());
        int right;
        IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        RECT tr;
        if (tray != IntPtr.Zero && GetWindowRect(tray, out tr) && tr.L > tb.L) right = tr.L - 4;
        else right = tb.R - (int)(TaskbarHeight * 5.5);
        int x = right - Cfg.Offset - L.Width, y = tb.T;
        // Most ticks change nothing visible (e.g. idle network), so skip the repaint entirely.
        if (force || L.Key != lastKey || x != lastX || y != lastY)
        {
            Push(L, TaskbarHeight, x, y);
            lastKey = L.Key; lastX = x; lastY = y;
        }
        UpdateTooltip(Sampler.Snap);
        ShowWindow(Handle, 8); // SW_SHOWNA
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // stay above the taskbar
    }

    void UpdateTooltip(Snapshot s)
    {
        var sb = new StringBuilder();
        if (s.CpuName != "") sb.AppendLine("CPU: " + s.CpuName);
        if (s.CpuTempStatus != "" && (Cfg.CpuTemp || Cfg.CpuPower)) sb.AppendLine("CPU temp: " + s.CpuTempStatus);
        if (s.HasGpu) sb.AppendLine("GPU: " + s.GpuName);
        if (Cfg.Ping) sb.AppendLine("Ping: " + Cfg.PingHost);
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

    // Keeps an open Settings page in step with changes made from a right-click menu.
    public void NotifySettings(string page)
    {
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshIfShowing(page);
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
        menu.Items.Add("PC Stats Bar settings…", null, (s, e) => bar.OpenSettings(null));
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
        if (disposing) { DisposeFonts(); brush.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }

    public void HideNow() { if (IsHandleCreated) ShowWindow(Handle, 0); }

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
        if (id == "Up" || id == "Down" || id == "NetTotal" || id == "Ping") return "Network";
        if (id.StartsWith("Cpu")) return "Processor";
        if (id.StartsWith("Ram")) return "Memory";
        if (id.StartsWith("Gpu")) return "Graphics";
        if (id.StartsWith("Disk")) return "Storage";
        if (id == "Processes" || id == "Uptime") return "System";
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
            it.Icon(g, new RectangleF(x0, y + (lineH - isz) / 2, isz, isz), it.Color);
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

// ======================================================================= Settings UI
static class Theme
{
    public static readonly Color Bg = Color.FromArgb(28, 28, 32), Nav = Color.FromArgb(22, 22, 26), Card = Color.FromArgb(38, 38, 44);
    public static readonly Color CardHover = Color.FromArgb(46, 46, 53), Border = Color.FromArgb(56, 56, 64);
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
    public Row(IconFn icon, Color iconColor, string title, string desc, Control right, int width)
    {
        this.icon = icon; this.iconColor = iconColor; this.title = title; this.desc = desc;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(width, string.IsNullOrEmpty(desc) ? 48 : 58);
        Margin = new Padding(0, 0, 0, 4);
        BackColor = Theme.Bg;
        if (right != null)
        {
            right.Location = new Point(Width - right.Width - 16, (Height - right.Height) / 2);
            right.Anchor = AnchorStyles.Right;
            Controls.Add(right);
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
            if (string.IsNullOrEmpty(desc)) g.DrawString(title, f, tb, x, (Height - f.GetHeight(g)) / 2);
            else { g.DrawString(title, f, tb, x, 9); g.DrawString(desc, sf, sb, x, 31); }
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
        Text = "PC Stats Bar";
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
        var brand = new Label { Text = "PC Stats Bar", Font = Theme.UI(13f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = false, Height = 64, Dock = DockStyle.Top, Padding = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        var version = new Label { Text = "v" + VersionText, Font = Theme.UI(8.5f), ForeColor = Theme.Sub, AutoSize = false, Height = 36, Dock = DockStyle.Bottom, Padding = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        var navList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10, 0, 10, 0), BackColor = Theme.Nav };
        foreach (var p in new[] { "Stats", "Arrange", "Appearance", "Colours", "Widget", "General", "About" }) navList.Controls.Add(NavItem(p));
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
        { "Stats", Icons.Chip }, { "Arrange", Icons.Grip }, { "Appearance", Icons.Text }, { "Colours", Icons.Palette }, { "Widget", Icons.Monitor }, { "General", Icons.Gear }, { "About", Icons.Info }
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
        Stat("CpuTemp", Program.IsAdmin ? "Via LibreHardwareMonitor sensors" : "Needs admin – see General → Run as administrator");
        Stat("CpuPower", Program.IsAdmin ? "Package power in watts" : "Needs admin");

        Header("Memory");
        Stat("Ram"); Stat("RamGb");
        Stat("RamCommit", "Memory promised to apps, including the page file");

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
        Stat("GpuClock", "Core clock in MHz");
        Stat("GpuFan", "Percent, or RPM when that's all the card reports");
        Stat("GpuVram"); Stat("GpuVramPct"); Stat("GpuPower");

        Header("Storage");
        Stat("Disk", "How busy the drives are");
        Stat("DiskRead"); Stat("DiskWrite");
        Stat("DiskFree", "Turns to the warning colour when space runs low");
        var drives = new List<string>();
        try { drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name).ToList(); } catch { }
        var driveChoices = new List<string> { "Windows drive" };
        driveChoices.AddRange(drives);
        Add(Icons.Disk, c.DiskColor, "Drive for free space", null,
            Combo(driveChoices.ToArray(), c.FreeDrive == "" ? 0 : driveChoices.IndexOf(c.FreeDrive), i => { c.FreeDrive = i == 0 ? "" : driveChoices[i]; Apply(); }, 150));

        Header("System");
        Stat("Processes");
        Stat("Uptime", "Time since Windows started");

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
            if (MessageBox.Show(this, "Put every stat back in its original order and remove all dividers?", "PC Stats Bar", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
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

    static string VersionText { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }

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
        list.Controls.Add(new Label { Text = "PC Stats Bar", Font = Theme.UI(16f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = false, Size = new Size(RowW, 36), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 0, 0) });
        Hint("A lightweight stats overlay for the Windows taskbar.", 28);
        Add(Icons.Info, Theme.Accent, "Version", null, new Label { Text = VersionText, AutoSize = false, Size = new Size(120, 24), TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Text, BackColor = Theme.Card, Font = Theme.UI(10f) });
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
            if (MessageBox.Show(this, "Put the widget's stats back to the default selection and order, and remove its dividers?", "PC Stats Bar", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
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

    void BuildGeneral()
    {
        Header("Behaviour");
        var intervals = new[] { 500, 1000, 2000, 3000, 5000 };
        Add(Icons.Gauge, Theme.Accent, "Update interval", "Slower updates use less CPU",
            Combo(intervals.Select(i => (i / 1000.0) + " s").ToArray(), Array.IndexOf(intervals, c.Interval), i => { c.Interval = intervals[i]; Apply(); }, 110));
        var off = new NumericUpDown { Minimum = 0, Maximum = 3000, Increment = 10, Value = Math.Max(0, Math.Min(3000, c.Offset)), Width = 90, BackColor = Theme.CardHover, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        off.ValueChanged += (s, e) => { c.Offset = (int)off.Value; Apply(); };
        Add(Icons.Spacing, Theme.Accent, "Shift left", "Pixels between the bar and the tray icons", off);
        Switch(Icons.Chip, Theme.Accent, "Left-click opens Task Manager", "ClickTaskMgr");
        Switch(Icons.Monitor, Theme.Accent, "Hide in fullscreen apps", "HideFullscreen", "Games, videos and presentations");

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

        Header("Startup & permissions");
        var startup = new Toggle { On = Program.IsStartupEnabled() };
        startup.Changed += (s, e) => { Program.SetStartup(startup.On); startup.On = Program.IsStartupEnabled(); };
        Add(Icons.Bolt, Theme.Accent, "Run at startup", Program.IsAdmin ? "Starts with admin rights at login (no UAC prompt)" : "Restart as admin first to enable CPU temp at startup", startup);
        if (Program.IsAdmin)
            Add(Icons.Thermo, c.TempColor, "Running as administrator", "CPU temperature and power are available", null);
        else
            Add(Icons.Thermo, c.TempColor, "Run as administrator", "Required to read CPU temperature and power", Btn("Restart as admin", (s, e) => Program.RestartAsAdmin(), true));

        Header("Reset");
        Add(Icons.Gear, Theme.Sub, "Restore default settings", "Every option, including the order and custom icons", Btn("Reset", (s, e) =>
        {
            if (MessageBox.Show(this, "Reset every setting to its default?", "PC Stats Bar", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            c.ResetToDefaults(); Apply(); RefreshPage();
        }));
    }
}
