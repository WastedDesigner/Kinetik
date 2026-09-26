// PC Stats Bar: a TrafficMonitor-style overlay that sits on the taskbar, left of the tray.
// Shows network speed, CPU (usage, clock, temp, power, per-core), RAM, GPU (usage, temp, VRAM, power),
// disk activity, and battery levels of the PC and connected Bluetooth devices.
// CPU temperature/power come from LibreHardwareMonitorLib (embedded in the exe) and need admin rights.
// Right-click the bar for Settings. Build: build.bat (uses the .NET Framework compiler built into Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ======================================================================= Settings
class Settings
{
    const string Key = @"Software\PCStatsBar";

    // What to show
    public bool Up = true, Down = true, Sep1 = false, Sep2 = false, Sep3 = false;
    public string Order = DefaultOrder;
    public bool Cpu = true, CpuClock = true, CpuTemp = true, CpuPower = false, CpuCores = false;
    public bool Ram = true, RamGb = true;
    public bool Gpu = true, GpuTemp = true, GpuVram = false, GpuPower = false;
    public string GpuSource = ""; // "" = auto, otherwise the GPU's name
    public bool Disk = false, Batteries = true, PcBattery = true, DeviceNames = false;

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
    // Behaviour
    public int Interval = 1000, Offset = 0;
    public bool ClickTaskMgr = true, HideFullscreen = true;

    // Every item that can appear on the bar, in default order. Each id is also the name of its on/off field.
    public const string DefaultOrder = "Up,Down,Sep1,Cpu,CpuCores,CpuClock,CpuTemp,CpuPower,Sep2,Ram,RamGb,Gpu,GpuTemp,GpuVram,GpuPower,Disk,Sep3,PcBattery,Batteries";

    public List<string> OrderList()
    {
        var all = DefaultOrder.Split(',');
        var list = (Order ?? "").Split(',').Where(all.Contains).Distinct().ToList();
        list.AddRange(all.Where(id => !list.Contains(id)));
        return list;
    }

    public bool IsOn(string id) { return (bool)GetType().GetField(id).GetValue(this); }

    static string DefaultFont()
    {
        foreach (var f in new[] { "Cascadia Mono", "Consolas" })
            if (FontFamily.Families.Any(x => x.Name == f)) return f;
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
                    else if (f.FieldType == typeof(float)) f.SetValue(this, float.Parse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture));
                    else if (f.FieldType == typeof(string)) f.SetValue(this, v.ToString());
                    else if (f.FieldType == typeof(Color)) f.SetValue(this, Color.FromArgb(Convert.ToInt32(v)));
                }
                catch { }
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
                else if (v is float) k.SetValue(f.Name, ((float)v).ToString(System.Globalization.CultureInfo.InvariantCulture));
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
}

// ======================================================================= Sampling
class Snapshot
{
    public double Cpu, CpuMhz, Up, Down, Disk = -1;
    public double[] Cores = new double[0];
    public double CpuTemp = double.NaN, CpuPower = double.NaN;
    public uint RamLoad; public double RamUsed, RamTotal;
    public bool HasGpu; public string GpuName = "";
    public double GpuUtil, GpuTemp, VramUsed, VramTotal, GpuPower, GpuMhz;
    public string CpuName = "", CpuTempStatus = "";
}

// Reads sensors through LibreHardwareMonitorLib: CPU temperature/power (needs admin + PawnIO driver)
// and AMD / Intel GPUs (no admin needed). NVIDIA GPUs are read directly through the driver's nvml.dll.
// Kept in its own class so the library is only loaded once the embedded-assembly resolver is in place.
class HwSensors
{
    LibreHardwareMonitor.Hardware.Computer computer;
    LibreHardwareMonitor.Hardware.IHardware cpu;
    List<LibreHardwareMonitor.Hardware.IHardware> gpus = new List<LibreHardwareMonitor.Hardware.IHardware>();
    string bestGpu;
    public string Error = "";

    public bool HasCpu { get { return cpu != null; } }
    public List<string> GpuNames { get { return gpus.Select(g => g.Name).ToList(); } }

    public bool Open(bool withCpu)
    {
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

        double util = Value(g, L, "GPU Core", "D3D 3D");
        double temp = Value(g, T, "GPU Core", "GPU Hot Spot");
        if (double.IsNaN(temp)) temp = Value(g, T);
        double used = Value(g, D, "GPU Memory Used", "D3D Dedicated Memory Used");
        double total = Value(g, D, "GPU Memory Total", "D3D Dedicated Memory Total");
        double power = Value(g, P, "GPU Package", "GPU Core");
        if (double.IsNaN(power)) power = Value(g, P);
        double mhz = Value(g, C, "GPU Core");

        s.HasGpu = true; s.GpuName = g.Name;
        s.GpuUtil = double.IsNaN(util) ? 0 : util;
        s.GpuTemp = double.IsNaN(temp) ? 0 : temp;
        s.VramUsed = double.IsNaN(used) ? 0 : used / 1024; // MB → GB
        s.VramTotal = double.IsNaN(total) ? 0 : total / 1024;
        s.GpuPower = double.IsNaN(power) ? 0 : power;
        s.GpuMhz = double.IsNaN(mhz) ? 0 : mhz;
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

class Sampler
{
    [StructLayout(LayoutKind.Sequential)] class MEMSTATUS
    {
        public uint Length = (uint)Marshal.SizeOf(typeof(MEMSTATUS)), Load;
        public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);

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

    public volatile Snapshot Snap = new Snapshot();
    public volatile Dictionary<string, int> Devices = new Dictionary<string, int>();
    readonly Settings cfg;
    readonly Action onSample;

    PerformanceCounter cpuTotal, cpuPerf, disk;
    List<PerformanceCounter> cores = new List<PerformanceCounter>();
    double maxMhz;
    string cpuName = "";
    IntPtr gpu = IntPtr.Zero; string gpuName = "";
    long lastRx, lastTx; DateTime lastNet = DateTime.MinValue;
    HwSensors sensors; string sensorStatus = "";

    public Sampler(Settings cfg, Action onSample) { this.cfg = cfg; this.onSample = onSample; }

    public void Start()
    {
        new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        new System.Threading.Timer(_ => ReadBatteries(), null, 0, 60000);    }

    void Init()
    {
        try { cpuTotal = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); cpuTotal.NextValue(); }
        catch { try { cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpuTotal.NextValue(); } catch { cpuTotal = null; } }
        try { cpuPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"); cpuPerf.NextValue(); } catch { cpuPerf = null; }
        try { disk = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); disk.NextValue(); } catch { disk = null; }
        try
        {
            var names = new PerformanceCounterCategory("Processor Information").GetInstanceNames()
                .Where(n => !n.Contains("_Total"))
                .OrderBy(n => int.Parse(n.Split(',')[0])).ThenBy(n => int.Parse(n.Split(',')[1]));
            foreach (var n in names)
            {
                var c = new PerformanceCounter("Processor Information", "% Processor Utility", n);
                c.NextValue(); cores.Add(c);
            }
        }
        catch { }
        try
        {
            using (var s = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_Processor"))
                foreach (ManagementObject o in s.Get()) { maxMhz = Convert.ToDouble(o["MaxClockSpeed"]); cpuName = o["Name"].ToString().Trim(); break; }
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
        OpenSensors();
    }

    // GPU sensors work without admin; CPU sensors are only enabled when running elevated.
    void OpenSensors()
    {
        if (!Program.IsAdmin) sensorStatus = "needs admin – right-click → Restart as administrator";
        try
        {
            sensors = new HwSensors();
            if (!sensors.Open(Program.IsAdmin)) { sensorStatus = "unavailable (" + sensors.Error + ")"; sensors = null; }
            else if (sensors.Error != "") sensorStatus = "unavailable (" + sensors.Error + ")";
        }
        catch (Exception e) { sensorStatus = "unavailable (" + e.Message + ")"; sensors = null; }
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

    Snapshot Sample()
    {
        var s = new Snapshot { CpuName = cpuName };
        if (cpuTotal != null) s.Cpu = Clamp(cpuTotal.NextValue());
        if (cpuPerf != null && maxMhz > 0) s.CpuMhz = cpuPerf.NextValue() * maxMhz / 100;
        if (disk != null) s.Disk = Clamp(100 - disk.NextValue());
        if (cfg.CpuCores) s.Cores = cores.Select(c => { try { return Clamp(c.NextValue()); } catch { return 0.0; } }).ToArray();

        var mem = new MEMSTATUS();
        if (GlobalMemoryStatusEx(mem))
        {
            s.RamLoad = mem.Load;
            s.RamTotal = mem.TotalPhys / 1073741824.0;
            s.RamUsed = (mem.TotalPhys - mem.AvailPhys) / 1073741824.0;
        }

        long rx = 0, tx = 0;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                var st = n.GetIPStatistics();
                rx += st.BytesReceived; tx += st.BytesSent;
            }
        }
        catch { }
        var now = DateTime.UtcNow;
        double secs = (now - lastNet).TotalSeconds;
        if (lastNet != DateTime.MinValue && secs > 0)
        {
            s.Down = Math.Max(0, (rx - lastRx) / secs);
            s.Up = Math.Max(0, (tx - lastTx) / secs);
        }
        lastRx = rx; lastTx = tx; lastNet = now;

        if (cfg.Gpu || cfg.GpuTemp || cfg.GpuVram || cfg.GpuPower) ReadGpu(s);

        if (sensors != null && sensors.HasCpu && (cfg.CpuTemp || cfg.CpuPower))
        {
            try
            {
                sensors.ReadCpu(out s.CpuTemp, out s.CpuPower);
                sensorStatus = double.IsNaN(s.CpuTemp) ? "no temperature sensor reported (is the PawnIO driver installed?)" : "";
            }
            catch (Exception e) { sensorStatus = "read failed (" + e.Message + ")"; }
        }
        s.CpuTempStatus = sensorStatus;
        return s;
    }

    // All GPUs that can be shown: the NVIDIA card (via NVML) first, then AMD / Intel cards.
    public List<string> GpuNames
    {
        get
        {
            var list = new List<string>();
            if (gpu != IntPtr.Zero) list.Add(gpuName);
            if (sensors != null) list.AddRange(sensors.GpuNames);
            return list;
        }
    }

    // Auto (empty choice) prefers the NVIDIA card, otherwise the AMD / Intel card with the most VRAM.
    void ReadGpu(Snapshot s)
    {
        string want = cfg.GpuSource ?? "";
        if (gpu != IntPtr.Zero && (want == "" || want == gpuName))
        {
            s.HasGpu = true; s.GpuName = gpuName;
            uint t, mw, mhz; NvUtil u; NvMem m;
            if (nvmlDeviceGetTemperature(gpu, 0, out t) == 0) s.GpuTemp = t;
            if (nvmlDeviceGetUtilizationRates(gpu, out u) == 0) s.GpuUtil = u.Gpu;
            if (nvmlDeviceGetMemoryInfo(gpu, out m) == 0) { s.VramUsed = m.Used / 1073741824.0; s.VramTotal = m.Total / 1073741824.0; }
            if (nvmlDeviceGetPowerUsage(gpu, out mw) == 0) s.GpuPower = mw / 1000.0;
            if (nvmlDeviceGetClockInfo(gpu, 0, out mhz) == 0) s.GpuMhz = mhz;
            return;
        }
        if (sensors != null)
            try { sensors.ReadGpu(want == gpuName ? "" : want, s); } catch { }
    }

    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // DEVPKEY_Bluetooth_Battery

    public void ReadBatteries()
    {
        var found = new Dictionary<string, int>();
        try
        {
            var q = "SELECT DeviceID, Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTH%' OR PNPDeviceID LIKE 'HID%' OR PNPDeviceID LIKE 'SWD%'";
            using (var searcher = new ManagementObjectSearcher(q))
                foreach (ManagementObject dev in searcher.Get())
                {
                    try
                    {
                        var args = new object[] { new[] { BatteryKey }, null };
                        dev.InvokeMethod("GetDeviceProperties", args);
                        var props = args[1] as ManagementBaseObject[];
                        if (props == null || props.Length == 0 || props[0]["Data"] == null) continue;
                        var name = (dev["Name"] ?? "Device").ToString();
                        if (!found.ContainsKey(name)) found[name] = Convert.ToInt32(props[0]["Data"]);
                    }
                    catch { }
                }
        }
        catch { }
        Devices = found;
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

    public static void Text(Graphics g, RectangleF r, Color c)
    {
        float s = r.Width;
        using (var p = P(c, s * 1.1f))
        {
            g.DrawLine(p, r.X + s * 0.15f, r.Y + s * 0.18f, r.X + s * 0.85f, r.Y + s * 0.18f);
            g.DrawLine(p, r.X + s * 0.5f, r.Y + s * 0.18f, r.X + s * 0.5f, r.Y + s * 0.85f);
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
    public class Info { public string Title; public IconFn Icon; public Color Color; }

    public static Info Get(string id, Settings c)
    {
        switch (id)
        {
            case "Up": return I("Upload speed", Icons.Up, c.UpColor);
            case "Down": return I("Download speed", Icons.Down, c.DownColor);
            case "Cpu": return I("CPU usage", Icons.Chip, c.CpuColor);
            case "CpuCores": return I("CPU per-core bars", Icons.Grid, c.CpuColor);
            case "CpuClock": return I("CPU clock speed", Icons.Gauge, c.CpuColor);
            case "CpuTemp": return I("CPU temperature", Icons.Thermo, c.TempColor);
            case "CpuPower": return I("CPU power draw", Icons.Bolt, c.CpuColor);
            case "Ram": return I("RAM usage %", Icons.Ram, c.RamColor);
            case "RamGb": return I("RAM used (GB)", Icons.Layers, c.RamColor);
            case "Gpu": return I("GPU usage", Icons.Gpu, c.GpuColor);
            case "GpuTemp": return I("GPU temperature", Icons.Flame, c.GpuColor);
            case "GpuVram": return I("VRAM used (GB)", Icons.Vram, c.GpuColor);
            case "GpuPower": return I("GPU power draw", Icons.Plug, c.GpuColor);
            case "Disk": return I("Disk activity", Icons.Disk, c.DiskColor);
            case "PcBattery": return I("PC battery", (g, r, col) => Icons.Battery(g, r, col, 70), c.BatColor);
            case "Batteries": return I("Bluetooth device batteries", Icons.Headphones, c.BatColor);
            default: return I("Divider " + id.Substring(3), Icons.Divider, c.LabelColor);
        }
    }

    static Info I(string t, IconFn i, Color c) { return new Info { Title = t, Icon = i, Color = c }; }
}

// ======================================================================= Program
static class Program
{
    public const string AppName = "PCStatsBar";
    public static readonly bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>();

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
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
            if (args.Contains("--settings")) bar.Load += (s, e) => bar.BeginInvoke((Action)bar.OpenSettings);
            Application.Run(bar);
        }
    }

    static bool Wait(Mutex m)
    {
        try { return m.WaitOne(15000); } catch (AbandonedMutexException) { return true; }
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
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit(15000);
            return p.ExitCode;
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
        if (IsAdmin)
        {
            var ps = "$a=New-ScheduledTaskAction -Execute '" + Application.ExecutablePath.Replace("'", "''") + "';" +
                     "$t=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME;" +
                     "$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0;" +
                     "Register-ScheduledTask -TaskName '" + AppName + "' -Action $a -Trigger $t -Settings $s -RunLevel Highest -Force";
            if (Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" + ps.Replace("\"", "\\\"") + "\"") == 0) return;
        }
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
    }
}

// ======================================================================= Bar
class StatsBar : Form
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLEND b, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
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
        menu.Items.Add("Settings…", null, (s, e) => OpenSettings());
        menu.Items.Add("Refresh batteries now", null, (s, e) => ThreadPool.QueueUserWorkItem(_ => Sampler.ReadBatteries()));
        if (!Program.IsAdmin) menu.Items.Add("Restart as administrator (CPU temp)", null, (s, e) => Program.RestartAsAdmin());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Close());

        Sampler = new Sampler(Cfg, () => { try { BeginInvoke((Action)Render); } catch { } });
        HandleCreated += (s, e) => Sampler.Start();
    }

    public void OpenSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(this);
        settingsForm.Show(); settingsForm.Activate();
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
        public IconFn Icon; public Color IconColor;
        public List<Part> Parts = new List<Part>();
        public double[] Bars; public Color BarColor;
        public bool IsSep;
        public float W;
    }

    static string Speed(double b)
    {
        if (b >= 1073741824) return (b / 1073741824).ToString("0.00") + " GB/s";
        if (b >= 1048576) return (b / 1048576).ToString(b >= 104857600 ? "0" : b >= 10485760 ? "0.0" : "0.00") + " MB/s";
        if (b >= 1024) return (b / 1024).ToString("0") + " KB/s";
        return b.ToString("0") + " B/s";
    }

    List<Seg> BuildSegments(Snapshot s, Color val, Color dim)
    {
        var c = Cfg;
        var segs = new List<Seg>();
        Func<double, double, Color> warn = (v, limit) => v >= limit ? c.WarnColor : val;
        Action<IconFn, Color, string, string, Color> add = (icon, iconColor, text, template, color) =>
            segs.Add(new Seg { Icon = icon, IconColor = iconColor, Parts = { new Part { Text = text, Template = template, Color = color, Bold = color != dim } } });

        foreach (var id in c.OrderList())
        {
            if (!c.IsOn(id)) continue;
            var info = Stats.Get(id, c);
            IconFn ic = info.Icon; Color col = info.Color;
            switch (id)
            {
                case "Up": add(ic, col, Speed(s.Up), "88.8 MB/s", val); break;
                case "Down": add(ic, col, Speed(s.Down), "88.8 MB/s", val); break;
                case "Cpu": add(ic, col, s.Cpu.ToString("0") + "%", "100%", warn(s.Cpu, 90)); break;
                case "CpuCores": if (s.Cores.Length > 0) segs.Add(new Seg { Icon = ic, IconColor = col, Bars = s.Cores, BarColor = col }); break;
                case "CpuClock": if (s.CpuMhz > 0) add(ic, col, (s.CpuMhz / 1000).ToString("0.00") + " GHz", "8.88 GHz", val); break;
                case "CpuTemp": add(ic, col, double.IsNaN(s.CpuTemp) ? "--°C" : s.CpuTemp.ToString("0") + "°C", "100°C", double.IsNaN(s.CpuTemp) ? dim : warn(s.CpuTemp, 85)); break;
                case "CpuPower": if (!double.IsNaN(s.CpuPower)) add(ic, col, s.CpuPower.ToString("0") + " W", "888 W", val); break;
                case "Ram": add(ic, col, s.RamLoad + "%", "100%", warn(s.RamLoad, 90)); break;
                case "RamGb": add(ic, col, s.RamUsed.ToString("0.0") + "/" + s.RamTotal.ToString("0") + " GB", "88.8/88 GB", val); break;
                case "Gpu": if (s.HasGpu) add(ic, col, s.GpuUtil.ToString("0") + "%", "100%", warn(s.GpuUtil, 95)); break;
                case "GpuTemp": if (s.HasGpu) add(ic, col, s.GpuTemp.ToString("0") + "°C", "100°C", warn(s.GpuTemp, 83)); break;
                case "GpuVram": if (s.HasGpu) add(ic, col, s.VramUsed.ToString("0.0") + "/" + s.VramTotal.ToString("0") + " GB", "88.8/88 GB", val); break;
                case "GpuPower": if (s.HasGpu) add(ic, col, s.GpuPower.ToString("0") + " W", "888 W", val); break;
                case "Disk": if (s.Disk >= 0) add(ic, col, s.Disk.ToString("0") + "%", "100%", warn(s.Disk, 95)); break;
                case "PcBattery":
                    var ps = SystemInformation.PowerStatus;
                    if (ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryLifePercent <= 1)
                    {
                        int p = (int)Math.Round(ps.BatteryLifePercent * 100);
                        bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
                        add((g, r, cc) => Icons.Battery(g, r, cc, p), col, (charging ? "+" : "") + p + "%", "+100%", p <= 20 ? c.WarnColor : val);
                    }
                    break;
                case "Batteries":
                    foreach (var d in Sampler.Devices)
                    {
                        int p = d.Value;
                        var seg = new Seg { Icon = Icons.ForDevice(d.Key, p), IconColor = p <= 20 ? c.WarnColor : col };
                        if (c.DeviceNames) seg.Parts.Add(new Part { Text = d.Key.Length > 14 ? d.Key.Substring(0, 13).TrimEnd() + "…" : d.Key, Template = "", Color = dim });
                        seg.Parts.Add(new Part { Text = p + "%", Template = "100%", Color = p <= 20 ? c.WarnColor : val, Bold = true });
                        segs.Add(seg);
                    }
                    break;
                default: // Sep1..Sep3
                    if (segs.Count > 0 && !segs[segs.Count - 1].IsSep) segs.Add(new Seg { IsSep = true, IconColor = dim });
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

    public static bool LightTaskbar()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
        }
        catch { return false; }
    }

    // Draws the bar into a transparent bitmap. Used for the taskbar overlay and the settings preview.
    public Bitmap RenderBitmap(int height, bool light)
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
        using (var font = new Font(Cfg.FontName, fontPx, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var bold = new Font(Cfg.FontName, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
        {
            fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            float icon = (float)Math.Round(fontPx * 1.15f), iconGap = fontPx * 0.4f, partGap = fontPx * 0.45f;
            float segGap = fontPx * 1.1f, pad = fontPx * 0.9f;
            float barW = Math.Max(2, fontPx * 0.22f), barGap = Math.Max(1, barW * 0.45f);

            using (var tmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmp))
                foreach (var seg in segs)
                {
                    if (seg.IsSep) { seg.W = 2; continue; }
                    float w = seg.Icon != null ? icon + iconGap : 0;
                    if (seg.Bars != null) w += seg.Bars.Length * (barW + barGap) - barGap;
                    for (int i = 0; i < seg.Parts.Count; i++)
                    {
                        var p = seg.Parts[i];
                        var f = p.Bold ? bold : font;
                        p.W = Math.Max(g.MeasureString(p.Text, f, PointF.Empty, fmt).Width,
                                       string.IsNullOrEmpty(p.Template) ? 0 : g.MeasureString(p.Template, f, PointF.Empty, fmt).Width);
                        w += p.W + (i > 0 ? partGap : 0);
                    }
                    seg.W = (float)Math.Ceiling(w);
                }

            // One segment per column, or two stacked in two-line mode. Dividers always get a column of their own.
            var cols = new List<List<Seg>>();
            foreach (var seg in segs)
            {
                var last = cols.Count > 0 ? cols[cols.Count - 1] : null;
                if (Cfg.TwoLines && !seg.IsSep && last != null && last.Count == 1 && !last[0].IsSep) last.Add(seg);
                else cols.Add(new List<Seg> { seg });
            }
            int width = (int)Math.Ceiling(pad * 2 + cols.Sum(col => col.Max(sg => sg.W)) + segGap * (cols.Count - 1));

            var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(1, 0, 0, 0)); // near-invisible, but still catches mouse clicks
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                if (Cfg.Pill && Cfg.PillOpacity > 0)
                {
                    float ph = Cfg.TwoLines ? height - 6 : Math.Min(height - 8, fontPx * 2.3f);
                    var pr = new RectangleF(1, (height - ph) / 2, width - 2, ph);
                    using (var path = Icons.Round(pr, Math.Min(ph / 2, fontPx * 0.8f)))
                    using (var b = new SolidBrush(Color.FromArgb((int)(Cfg.PillOpacity * 2.55), Cfg.PillColor)))
                        g.FillPath(b, path);
                }

                float lineH = font.GetHeight(g), rowH = Math.Max(lineH, icon);
                float cx = pad;
                foreach (var col in cols)
                {
                    for (int row = 0; row < col.Count; row++)
                    {
                        var seg = col[row];
                        if (seg.IsSep)
                        {
                            float sh = Cfg.TwoLines ? height * 0.62f : icon * 1.5f;
                            using (var sp = new Pen(Color.FromArgb(110, seg.IconColor), 1.5f))
                                g.DrawLine(sp, cx + 1, (height - sh) / 2, cx + 1, (height + sh) / 2);
                            continue;
                        }
                        float cy = col.Count == 1 ? (height - rowH) / 2 : (row == 0 ? height / 2f - rowH : height / 2f);
                        float sx = cx;
                        if (seg.Icon != null)
                        {
                            seg.Icon(g, new RectangleF(sx, cy + (rowH - icon) / 2, icon, icon), seg.IconColor);
                            sx += icon + iconGap;
                        }
                        if (seg.Bars != null)
                        {
                            float by = cy + (rowH - icon) / 2;
                            using (var bg = new SolidBrush(Color.FromArgb(55, seg.BarColor))) using (var fg = new SolidBrush(seg.BarColor))
                                for (int i = 0; i < seg.Bars.Length; i++)
                                {
                                    float x = sx + i * (barW + barGap), h = icon * (float)seg.Bars[i] / 100f;
                                    g.FillRectangle(bg, x, by, barW, icon);
                                    g.FillRectangle(fg, x, by + icon - h, barW, h);
                                }
                        }
                        for (int i = 0; i < seg.Parts.Count; i++)
                        {
                            var p = seg.Parts[i];
                            if (i > 0) sx += partGap;
                            using (var b = new SolidBrush(p.Color))
                                g.DrawString(p.Text, p.Bold ? bold : font, b, sx, cy + (rowH - lineH) / 2, fmt);
                            sx += p.W;
                        }
                    }
                    cx += col.Max(sg => sg.W) + segGap;
                }
            }
            return bmp;
        }
    }

    public void Render()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        RECT tb;
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out tb)) return;
        int qs;
        if (Cfg.HideFullscreen && SHQueryUserNotificationState(out qs) == 0 && (qs == 2 || qs == 3 || qs == 4))
        { ShowWindow(Handle, 0); return; }

        TaskbarHeight = tb.B - tb.T;
        using (var bmp = RenderBitmap(TaskbarHeight, LightTaskbar()))
        {
            int right;
            IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT tr;
            if (tray != IntPtr.Zero && GetWindowRect(tray, out tr) && tr.L > tb.L) right = tr.L - 4;
            else right = tb.R - (int)(TaskbarHeight * 5.5);
            Push(bmp, right - Cfg.Offset - bmp.Width, tb.T);
        }
        UpdateTooltip(Sampler.Snap);
        ShowWindow(Handle, 8); // SW_SHOWNA
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // stay above the taskbar
    }

    void UpdateTooltip(Snapshot s)
    {
        var sb = new StringBuilder();
        if (s.CpuName != "") sb.AppendLine("CPU: " + s.CpuName);
        if (s.CpuTempStatus != "") sb.AppendLine("CPU temp: " + s.CpuTempStatus);
        if (s.HasGpu) sb.AppendLine("GPU: " + s.GpuName);
        foreach (var d in Sampler.Devices) sb.AppendLine(d.Key + ": " + d.Value + "%");
        sb.Append("Left-click: Task Manager  ·  Right-click: Settings");
        var text = sb.ToString();
        if (text != lastTip) { lastTip = text; tip.SetToolTip(this, text); }
    }

    void Push(Bitmap bmp, int x, int y)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hb);
        var size = new SIZE { CX = bmp.Width, CY = bmp.Height };
        var src = new POINT();
        var pos = new POINT { X = x, Y = y };
        var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
        UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2);
        SelectObject(mem, old); DeleteObject(hb); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
    }
}

// ======================================================================= Settings UI
static class Theme
{
    public static readonly Color Bg = Color.FromArgb(28, 28, 32), Nav = Color.FromArgb(22, 22, 26), Card = Color.FromArgb(38, 38, 44);
    public static readonly Color CardHover = Color.FromArgb(46, 46, 53), Border = Color.FromArgb(56, 56, 64);
    public static readonly Color Text = Color.FromArgb(236, 236, 240), Sub = Color.FromArgb(150, 150, 162), Accent = Color.FromArgb(76, 194, 255);
    public static Font UI(float size, FontStyle st = FontStyle.Regular)
    {
        var fam = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        return new Font(fam, size, st);
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
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var path = Icons.Round(r, r.Height / 2))
        {
            if (on) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            else using (var p = new Pen(Theme.Sub, 1.5f)) g.DrawPath(p, path);
        }
        float d = r.Height - (on ? 8 : 10), x = on ? r.Right - d - 4 : r.X + 5;
        using (var b = new SolidBrush(on ? Theme.Bg : Theme.Sub)) g.FillEllipse(b, x, r.Y + (r.Height - d) / 2, d, d);
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

class ArrowButton : Control
{
    readonly bool up; bool hover;
    public ArrowButton(bool up)
    {
        this.up = up;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(28, 28); Cursor = Cursors.Hand; BackColor = Theme.Card; Margin = new Padding(2, 0, 0, 0);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Card);
        if (hover && Enabled) using (var path = Icons.Round(new RectangleF(0, 0, Width - 1, Height - 1), 6)) using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, path);
        var col = Enabled ? (hover ? Theme.Text : Theme.Sub) : Color.FromArgb(70, Theme.Sub);
        float cx = Width / 2f, cy = Height / 2f, w = 5f, h = 3f;
        using (var p = new Pen(col, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            if (up) g.DrawLines(p, new[] { new PointF(cx - w, cy + h), new PointF(cx, cy - h), new PointF(cx + w, cy + h) });
            else g.DrawLines(p, new[] { new PointF(cx - w, cy - h), new PointF(cx, cy + h), new PointF(cx + w, cy - h) });
    }
}

// A settings row: icon, title, optional description, and a control on the right.
class Row : Panel
{
    readonly IconFn icon; readonly Color iconColor; readonly string title, desc;
    public bool ShowGrip;
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
        using (var path = Icons.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8)) using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
        float x = 16;
        if (ShowGrip) { Icons.Grip(g, new RectangleF(8, (Height - 16) / 2f, 16, 16), Theme.Sub); x = 30; }
        if (icon != null) { icon(g, new RectangleF(x, (Height - 18) / 2f, 18, 18), iconColor); x += 34; }
        using (var f = Theme.UI(10f)) using (var sf = Theme.UI(8.5f)) using (var tb = new SolidBrush(Theme.Text)) using (var sb = new SolidBrush(Theme.Sub))
        {
            if (string.IsNullOrEmpty(desc)) g.DrawString(title, f, tb, x, (Height - f.GetHeight(g)) / 2);
            else { g.DrawString(title, f, tb, x, 9); g.DrawString(desc, sf, sb, x, 31); }
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
    readonly List<Label> navItems = new List<Label>();
    const int RowW = 540;
    string page = "Stats";

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
        ShowIcon = false;

        // Preview strip on top (looks like a slice of taskbar)
        var top = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Bg, Padding = new Padding(16, 14, 16, 10) };
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 36) };
        frame.Controls.Add(preview);
        top.Controls.Add(frame);

        // Navigation
        var brand = new Label { Text = "PC Stats Bar", Font = Theme.UI(13f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = false, Height = 64, Dock = DockStyle.Top, Padding = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        var navList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10, 0, 10, 0), BackColor = Theme.Nav };
        foreach (var p in new[] { "Stats", "Arrange", "Appearance", "Colours", "General" }) navList.Controls.Add(NavItem(p));
        nav.Controls.Add(navList); nav.Controls.Add(brand);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        right.Controls.Add(content); right.Controls.Add(top);
        Controls.Add(right); Controls.Add(nav);

        previewTimer.Tick += (s, e) => UpdatePreview();
        previewTimer.Start();
        FormClosed += (s, e) => previewTimer.Dispose();
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
        { "Stats", Icons.Chip }, { "Arrange", Icons.Grip }, { "Appearance", Icons.Text }, { "Colours", Icons.Palette }, { "General", Icons.Gear }
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
                using (var path = Icons.Round(new RectangleF(0, 0, l.Width - 1, l.Height - 1), 6)) using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (var path = Icons.Round(new RectangleF(0, 12, 3, l.Height - 24), 1.5f)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            }
            navIcons[name](g, new RectangleF(14, 11, 18, 18), sel ? Theme.Accent : Theme.Sub);
            using (var b = new SolidBrush(sel ? Theme.Text : Theme.Sub)) g.DrawString(name, l.Font, b, 44, (l.Height - l.Font.GetHeight(g)) / 2);
        };
        l.Click += (s, e) => ShowPage(name);
        navItems.Add(l);
        return l;
    }

    void Apply() { c.Save(); bar.Render(); UpdatePreview(); }

    void UpdatePreview()
    {
        var old = preview.Image;
        preview.Image = bar.RenderBitmap(Math.Max(40, bar.TaskbarHeight), false);
        if (old != null) old.Dispose();
    }

    // ---- page building helpers ----
    FlowLayoutPanel list;

    void Header(string text)
    {
        list.Controls.Add(new Label { Text = text, Font = Theme.UI(10f, FontStyle.Bold), ForeColor = Theme.Sub, AutoSize = false, Size = new Size(RowW, 36), TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(2, 6, 0, 6) });
    }

    void Add(IconFn icon, Color iconColor, string title, string desc, Control right)
    {
        list.Controls.Add(new Row(icon, iconColor, title, desc, right, RowW));
    }

    void Switch(IconFn icon, Color iconColor, string title, string field, string desc = null)
    {
        var f = typeof(Settings).GetField(field);
        var t = new Toggle { On = (bool)f.GetValue(c) };
        t.Changed += (s, e) => { f.SetValue(c, t.On); Apply(); };
        Add(icon, iconColor, title, desc, t);
    }

    void ColourRow(IconFn icon, string title, string field)
    {
        var f = typeof(Settings).GetField(field);
        var sw = new Swatch { Value = (Color)f.GetValue(c) };
        sw.Click += (s, e) =>
        {
            using (var dlg = new ColorDialog { Color = sw.Value, FullOpen = true })
                if (dlg.ShowDialog(this) == DialogResult.OK) { f.SetValue(c, dlg.Color); sw.Value = dlg.Color; Apply(); ShowPage(page); }
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
        b.Click += click;
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

    void ShowPage(string name)
    {
        page = name;
        foreach (var l in navItems) l.Invalidate();
        content.SuspendLayout();
        foreach (Control ctl in content.Controls.Cast<Control>().ToList()) ctl.Dispose();
        list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 0, 16, 16), BackColor = Theme.Bg };
        content.Controls.Add(list);

        if (name == "Stats") BuildStats();
        else if (name == "Arrange") BuildArrange();
        else if (name == "Appearance") BuildAppearance();
        else if (name == "Colours") BuildColours();
        else BuildGeneral();

        content.ResumeLayout();
        UpdatePreview();
    }

    void BuildStats()
    {
        Header("Network");
        Stat("Up"); Stat("Down");
        Header("Processor");
        Stat("Cpu");
        Stat("CpuCores", "A tiny usage bar for every logical core");
        Stat("CpuClock");
        Stat("CpuTemp", Program.IsAdmin ? "Via LibreHardwareMonitor sensors" : "Needs admin – see General → Run as administrator");
        Stat("CpuPower", Program.IsAdmin ? "Package power in watts" : "Needs admin");
        Header("Memory");
        Stat("Ram"); Stat("RamGb");
        Header("Graphics");
        var gpus = bar.Sampler.GpuNames;
        if (gpus.Count > 0)
        {
            var choices = new List<string> { "Auto" };
            choices.AddRange(gpus);
            int sel = Math.Max(0, choices.IndexOf(c.GpuSource));
            Add(Icons.Gpu, c.GpuColor, "Graphics card", "NVIDIA, AMD and Intel supported",
                Combo(choices.ToArray(), sel, i => { c.GpuSource = i == 0 ? "" : choices[i]; Apply(); }, 230));
        }
        else Add(Icons.Gpu, c.GpuColor, "No supported graphics card found", null, null);
        Stat("Gpu"); Stat("GpuTemp"); Stat("GpuVram"); Stat("GpuPower");
        Header("Storage");
        Stat("Disk");
        Header("Batteries");
        Stat("Batteries", "Headphones, mice, keyboards, controllers");
        Switch(Icons.Text, c.BatColor, "Show device names", "DeviceNames");
        Stat("PcBattery", "Laptops only");
    }

    void Stat(string id, string desc = null)
    {
        var info = Stats.Get(id, c);
        Switch(info.Icon, info.Color, info.Title, id, desc);
    }

    // ---- Arrange: reorder items (drag rows or use the arrows) and place dividers between groups.
    void BuildArrange()
    {
        list.Controls.Add(new Label
        {
            Text = "Drag rows or use the arrows to set the order on the taskbar (left → right). Turn on dividers to split stats into groups.",
            Font = Theme.UI(9.5f), ForeColor = Theme.Sub, AutoSize = false, Size = new Size(RowW, 48), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2, 6, 0, 4)
        });
        var order = c.OrderList();
        for (int i = 0; i < order.Count; i++)
        {
            string id = order[i];
            var info = Stats.Get(id, c);
            var controls = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty, Padding = Padding.Empty };
            var f = typeof(Settings).GetField(id);
            var t = new Toggle { On = (bool)f.GetValue(c), Margin = new Padding(0, 2, 12, 0) };
            t.Changed += (s, e) => { f.SetValue(c, t.On); Apply(); };
            int index = i;
            var up = new ArrowButton(true) { Enabled = i > 0 };
            up.Click += (s, e) => MoveItem(id, index - 1);
            var down = new ArrowButton(false) { Enabled = i < order.Count - 1 };
            down.Click += (s, e) => MoveItem(id, index + 1);
            controls.Controls.Add(t); controls.Controls.Add(up); controls.Controls.Add(down);

            bool isSep = id.StartsWith("Sep");
            var row = new Row(info.Icon, info.Color, info.Title, isSep ? "Thin line between groups" : null, controls, RowW) { ShowGrip = true, Tag = id, Cursor = Cursors.SizeAll };
            row.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) row.DoDragDrop(id, DragDropEffects.Move); };
            row.AllowDrop = true;
            row.DragOver += DragOverList;
            row.DragDrop += DropOnList;
            list.Controls.Add(row);
        }
        list.AllowDrop = true;
        list.DragOver += DragOverList;
        list.DragDrop += DropOnList;
    }

    void DragOverList(object sender, DragEventArgs e)
    {
        e.Effect = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None;
    }

    void DropOnList(object sender, DragEventArgs e)
    {
        var id = e.Data.GetData(typeof(string)) as string;
        if (id == null) return;
        var pt = list.PointToClient(new Point(e.X, e.Y));
        var rows = list.Controls.OfType<Row>().ToList();
        int target = rows.Count(r => r.Top + r.Height / 2 < pt.Y);
        int from = rows.FindIndex(r => (string)r.Tag == id);
        if (from < target) target--; // removing the item shifts later rows up
        MoveItem(id, target);
    }

    void MoveItem(string id, int index)
    {
        var order = c.OrderList();
        order.Remove(id);
        order.Insert(Math.Max(0, Math.Min(order.Count, index)), id);
        c.Order = string.Join(",", order);
        Apply();
        int scroll = list.VerticalScroll.Value;
        ShowPage(page);
        list.AutoScrollPosition = new Point(0, scroll);
    }

    void BuildAppearance()
    {
        Header("Text");
        var fontBtn = Btn(c.FontName, null);
        fontBtn.Click += (s, e) =>
        {
            using (var dlg = new FontDialog { Font = new Font(c.FontName, c.FontSize > 0 ? c.FontSize : 10f), ShowEffects = false, FontMustExist = true, AllowVerticalFonts = false })
                if (dlg.ShowDialog(this) == DialogResult.OK) { c.FontName = dlg.Font.Name; Apply(); ShowPage(page); }
        };
        Add(Icons.Text, Theme.Accent, "Font", "Monospace fonts keep the numbers from shifting", fontBtn);
        var sizes = new[] { 0f, 8, 9, 10, 11, 12, 13, 14, 16, 18 };
        Add(Icons.Text, Theme.Accent, "Font size", null,
            Combo(sizes.Select(v => v == 0 ? "Auto (fit taskbar)" : v + " pt").ToArray(), Array.IndexOf(sizes, c.FontSize), i => { c.FontSize = sizes[i]; Apply(); }));
        Header("Layout");
        Add(Icons.Grid, Theme.Accent, "Rows", null, Combo(new[] { "One line", "Two lines (compact)" }, c.TwoLines ? 1 : 0, i => { c.TwoLines = i == 1; Apply(); }));
        Switch(Icons.Palette, Theme.Accent, "Background pill", "Pill", "Rounded, see-through backdrop behind the stats");
        var op = new TrackBar { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Value = c.PillOpacity, Width = 180, BackColor = Theme.Card, AutoSize = false, Height = 26 };
        op.ValueChanged += (s, e) => { c.PillOpacity = op.Value; Apply(); };
        Add(Icons.Palette, Theme.Accent, "Background opacity", null, op);
    }

    void BuildColours()
    {
        Header("Text");
        Switch(Icons.Text, Theme.Accent, "Match taskbar theme", "AutoTheme", "White on dark taskbars, black on light. Turn off to pick your own.");
        ColourRow(Icons.Text, "Value text", "ValueColor");
        ColourRow(Icons.Text, "Secondary text", "LabelColor");
        ColourRow(Icons.Thermo, "Warning (high usage / temp, low battery)", "WarnColor");
        Header("Icons");
        ColourRow(Icons.Up, "Upload", "UpColor");
        ColourRow(Icons.Down, "Download", "DownColor");
        ColourRow(Icons.Chip, "CPU", "CpuColor");
        ColourRow(Icons.Thermo, "CPU temperature", "TempColor");
        ColourRow(Icons.Ram, "RAM", "RamColor");
        ColourRow(Icons.Gpu, "GPU", "GpuColor");
        ColourRow(Icons.Disk, "Disk", "DiskColor");
        ColourRow(Icons.Headphones, "Batteries", "BatColor");
        Header("Background");
        ColourRow(Icons.Palette, "Pill colour", "PillColor");
    }

    void BuildGeneral()
    {
        Header("Behaviour");
        var intervals = new[] { 500, 1000, 2000, 3000 };
        Add(Icons.Gauge, Theme.Accent, "Update interval", null,
            Combo(intervals.Select(i => (i / 1000.0) + " s").ToArray(), Array.IndexOf(intervals, c.Interval), i => { c.Interval = intervals[i]; Apply(); }, 110));
        var off = new NumericUpDown { Minimum = 0, Maximum = 3000, Increment = 10, Value = c.Offset, Width = 90, BackColor = Theme.CardHover, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        off.ValueChanged += (s, e) => { c.Offset = (int)off.Value; Apply(); };
        Add(Icons.Grid, Theme.Accent, "Shift left", "Pixels between the bar and the tray icons", off);
        Switch(Icons.Chip, Theme.Accent, "Left-click opens Task Manager", "ClickTaskMgr");
        Switch(Icons.Grid, Theme.Accent, "Hide in fullscreen apps", "HideFullscreen", "Games, videos and presentations");

        Header("Startup & permissions");
        var startup = new Toggle { On = Program.IsStartupEnabled() };
        startup.Changed += (s, e) => { Program.SetStartup(startup.On); startup.On = Program.IsStartupEnabled(); };
        Add(Icons.Bolt, Theme.Accent, "Run at startup", Program.IsAdmin ? "Starts with admin rights at login (no UAC prompt)" : "Restart as admin first to enable CPU temp at startup", startup);
        if (Program.IsAdmin)
            Add(Icons.Thermo, c.TempColor, "Running as administrator", "CPU temperature and power are available", null);
        else
            Add(Icons.Thermo, c.TempColor, "Run as administrator", "Required to read CPU temperature and power", Btn("Restart as admin", (s, e) => Program.RestartAsAdmin(), true));

        Header("Reset");
        Add(Icons.Gear, Theme.Sub, "Restore default settings", null, Btn("Reset", (s, e) => { c.ResetToDefaults(); Apply(); ShowPage(page); }));
    }
}
