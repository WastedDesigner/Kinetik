// Shared suite support. GPL-3.0-or-later. Synced into each standalone source folder by sync.py.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace KinetikSuite
{
    public static class Hashes
    {
        public static string Of(byte[] bytes) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", ""); }
        public static string FileHash(string path) { using (var h = SHA256.Create()) using (var f = File.OpenRead(path)) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", ""); }
    }

    public static class Resources
    {
        public static void DisposeValues<T>(IDictionary<string, T> resources) where T : IDisposable
        { foreach (var item in resources.Values) if (item != null) item.Dispose(); resources.Clear(); }
    }

    public sealed class TextDocument
    {
        public byte[] Bytes;
        public string Text, Revision;
        public Encoding Encoding;
        public static TextDocument Read(string path)
        {
            byte[] b;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (f.Length > 4 * 1024 * 1024) throw new IOException("This note is too large to edit safely.");
                using (var m = new MemoryStream()) { f.CopyTo(m); b = m.ToArray(); }
            }
            int skip = 0;
            Encoding enc = new UTF8Encoding(false, true);
            if (b.Length >= 4 && b[0] == 255 && b[1] == 254 && b[2] == 0 && b[3] == 0) { enc = new UTF32Encoding(false, true, true); skip = 4; }
            else if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 254 && b[3] == 255) { enc = new UTF32Encoding(true, true, true); skip = 4; }
            else if (b.Length >= 3 && b[0] == 239 && b[1] == 187 && b[2] == 191) { enc = new UTF8Encoding(true, true); skip = 3; }
            else if (b.Length >= 2 && b[0] == 255 && b[1] == 254) { enc = new UnicodeEncoding(false, true, true); skip = 2; }
            else if (b.Length >= 2 && b[0] == 254 && b[1] == 255) { enc = new UnicodeEncoding(true, true, true); skip = 2; }
            string text = enc.GetString(b, skip, b.Length - skip);
            if (text.IndexOf('\0') >= 0) throw new IOException("The note has an unsupported encoding.");
            return new TextDocument { Bytes = b, Text = text, Revision = Hashes.Of(b), Encoding = enc };
        }
        public byte[] Encode(string text) { return Encoding.GetPreamble().Concat(Encoding.GetBytes(text)).ToArray(); }
    }

    public static class SafeEdit
    {
        // Cooperating suite apps serialize edits. Other editors are checked by revision;
        // File.Replace also captures the exact displaced content in the recovery folder.
        public static void Update(string path, string expectedRevision, Func<TextDocument, string> edit)
        {
            path = Path.GetFullPath(path);
            var mutexName = "Kinetik.Edit." + Hashes.Of(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
            using (var mutex = new Mutex(false, mutexName))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(5000); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Another Kinetik app is editing this note. Try again.");
                    var doc = TextDocument.Read(path);
                    if (!string.IsNullOrEmpty(expectedRevision) && expectedRevision != doc.Revision)
                        throw new IOException("The note changed in another editor. Refresh and select the task again.");
                    var text = edit(doc);
                    if (text == null || text == doc.Text) return;
                    var temp = path + ".kinetik-" + Guid.NewGuid().ToString("N") + ".tmp";
                    var recoveryDir = Path.Combine(Path.GetDirectoryName(path), ".kinetik-recovery");
                    Directory.CreateDirectory(recoveryDir);
                    var recovery = Path.Combine(recoveryDir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".bak");
                    try
                    {
                        var bytes = doc.Encode(text);
                        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
                        if (Hashes.FileHash(path) != doc.Revision) throw new IOException("The note changed while saving. Nothing was replaced; refresh and try again.");
                        File.Replace(temp, path, recovery);
                        // A non-cooperating editor can replace the path in the final gap.
                        // Preserve and surface its displaced edit rather than silently losing it.
                        if (Hashes.FileHash(recovery) != doc.Revision)
                            throw new IOException("A simultaneous edit was detected. Both versions were kept. The other edit is at:\n" + recovery);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }
    }

    public static class Markdown
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct FindData
        {
            public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint SizeHigh, SizeLow, ReparseTag, Reserved;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateName;
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindFirstFileW(string path, out FindData data);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool FindClose(IntPtr handle);
        static bool DirectoryLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return true;
            FindData data; var handle = FindFirstFileW(path, out data);
            if (handle == new IntPtr(-1)) return true;
            try { return (data.ReparseTag & 0x20000000) != 0; } // Name-surrogate links, not OneDrive cloud placeholders.
            finally { FindClose(handle); }
        }
        public static IEnumerable<string> Files(string folder, bool recursive)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.md")) if (!Path.GetFileName(file).StartsWith(".")) yield return file;
            if (!recursive) yield break;
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name.Equals("Templates", StringComparison.OrdinalIgnoreCase)) continue;
                if (DirectoryLink(dir)) continue;
                foreach (var file in Files(dir, true)) yield return file;
            }
        }
        // CommonMark fences: opening fence character and minimum length must match.
        public static bool Fence(string line, ref char marker, ref int length)
        {
            var s = line.TrimStart();
            int n = 0;
            char c = s.Length > 0 ? s[0] : '\0';
            if (c != '`' && c != '~') return false;
            while (n < s.Length && s[n] == c) n++;
            if (n < 3) return false;
            if (marker == '\0') { marker = c; length = n; return true; }
            if (marker == c && n >= length && s.Substring(n).Trim().Length == 0) { marker = '\0'; length = 0; return true; }
            return false;
        }
    }

    public static class FileTransaction
    {
        // Inputs are relative paths. Every new file is fully written and flushed before
        // any installed file is moved. The backup is retained on success and on failure.
        public static void Install(string root, IDictionary<string, byte[]> files, string backup, Action<string, int> checkpoint = null)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(backup);
            var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var installed = new List<string>();
            int step = 0;
            try
            {
                foreach (var f in files)
                {
                    var target = Path.GetFullPath(Path.Combine(root, f.Key.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || staged.ContainsKey(target)) throw new IOException("Invalid or duplicate release path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    var temp = target + ".stage-" + Guid.NewGuid().ToString("N");
                    staged.Add(target, temp);
                    using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (checkpoint != null) checkpoint("write", step++);
                        fs.Write(f.Value, 0, f.Value.Length); fs.Flush(true);
                    }
                    if (Hashes.FileHash(temp) != Hashes.Of(f.Value)) throw new IOException("Staged release checksum mismatch.");
                }
                foreach (var f in staged)
                {
                    if (File.Exists(f.Key))
                    {
                        var old = Path.Combine(backup, f.Key.Substring(root.Length));
                        Directory.CreateDirectory(Path.GetDirectoryName(old));
                        File.Move(f.Key, old); moved.Add(f.Key, old);
                    }
                    if (checkpoint != null) checkpoint("commit", step++);
                    File.Move(f.Value, f.Key); installed.Add(f.Key);
                }
                File.WriteAllText(Path.Combine(backup, "transaction-status.txt"), "Committed. Previous files retained for recovery.");
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var target in installed.AsEnumerable().Reverse()) try { File.Delete(target); } catch (Exception e) { errors.Add(e); }
                foreach (var f in moved.Reverse()) try { File.Move(f.Value, f.Key); } catch (Exception e) { errors.Add(e); }
                try { File.WriteAllText(Path.Combine(backup, "transaction-status.txt"), "Failed: " + string.Join("\n", errors.Select(e => e.Message))); } catch { }
                throw new IOException("Installation failed. Recovery files: " + backup + ". " + string.Join("; ", errors.Select(e => e.Message)), failure);
            }
            finally { foreach (var temp in staged.Values) try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
    }

    public static class Http
    {
        public static byte[] Get(string url, int limit, string agent, CancellationToken cancel, int timeoutMs = 20000, string accept = null)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new IOException("A HTTP or HTTPS URL is required.");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = agent; request.Timeout = request.ReadWriteTimeout = timeoutMs;
            // gzip only: servers that answer "deflate" send it zlib-wrapped, which .NET Framework's DeflateStream can't read
            // ("Block length does not match with its complement").
            request.AutomaticDecompression = DecompressionMethods.GZip;
            if (accept != null) request.Accept = accept;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                deadline.CancelAfter(timeoutMs);
                using (deadline.Token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var result = new MemoryStream())
                {
                    if (uri.Scheme == "https" && response.ResponseUri.Scheme != "https") throw new IOException("The download redirected to an insecure URL.");
                    if (response.ContentLength > limit) throw new IOException("The response is too large.");
                    var buffer = new byte[16384]; int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        if (result.Length + read > limit) throw new IOException("The response is too large.");
                        result.Write(buffer, 0, read);
                    }
                    return result.ToArray();
                }
            }
        }
        public static string Text(string url, int limit, string agent, CancellationToken cancel, int timeoutMs = 20000, string accept = null)
        { return Encoding.UTF8.GetString(Get(url, limit, agent, cancel, timeoutMs, accept)); }
    }

    public static class SettingsValidation
    {
        static readonly Dictionary<string, int[]> Ranges = new Dictionary<string, int[]> {
            { "Width", new[] { 120, 2000 } }, { "Scale", new[] { 30, 400 } }, { "Opacity", new[] { 0, 100 } },
            { "Layer", new[] { 0, 2 } }, { "Style", new[] { 0, 2 } }, { "IconSize", new[] { 0, 2 } },
            { "Hours", new[] { 1, 24 } }, { "Days", new[] { 1, 31 } }, { "Refresh", new[] { 1, 1440 } },
            { "Wind", new[] { 0, 2 } }, { "FocusMin", new[] { 1, 240 } }, { "ShortMin", new[] { 1, 120 } },
            { "LongMin", new[] { 1, 240 } }, { "LongEvery", new[] { 1, 20 } }, { "Phase", new[] { 0, 2 } },
            { "Cycle", new[] { 0, 20 } }, { "Remaining", new[] { 0, 14400 } }, { "TaskLine", new[] { -1, 1000000 } },
            { "TaskScanLimit", new[] { 40, 10000 } }, { "RefreshMin", new[] { 1, 1440 } },
            { "SyncMin", new[] { 5, 1440 } }, { "MaxItems", new[] { 1, 500 } }, { "MaxAgeDays", new[] { 0, 3650 } },
            { "Round", new[] { 0, 400 } }, { "Density", new[] { 0, 2 } }, { "Marker", new[] { 0, 3 } },
            { "MonthStyle", new[] { 0, 2 } }, { "FirstDay", new[] { -1, 6 } }, { "TitleStyle", new[] { 0, 3 } },
            { "MaxPerDay", new[] { 0, 100 } }, { "MaxHeight", new[] { 0, 4000 } }
        };
        public static void Sanitize(object settings)
        {
            var fields = settings.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(string)).ToArray();
            var original = fields.Select(f => f.GetValue(settings)).ToArray();
            foreach (var f in settings.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                int[] range;
                if (f.FieldType == typeof(int) && Ranges.TryGetValue(f.Name, out range))
                    f.SetValue(settings, Math.Max(range[0], Math.Min(range[1], (int)f.GetValue(settings))));
                if (f.FieldType == typeof(string) && f.GetValue(settings) == null) f.SetValue(settings, "");
            }
            var until = settings.GetType().GetField("RunningUntil");
            if (until != null)
            {
                var raw = until.GetValue(settings) as string; DateTime d;
                if (raw != "" && (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d) || d.Kind != DateTimeKind.Utc || d > DateTime.UtcNow.AddHours(24))) until.SetValue(settings, "");
            }
            var lat = settings.GetType().GetField("Lat"); var lon = settings.GetType().GetField("Lon");
            if (lat != null && lon != null)
            {
                double a, b;
                if (!double.TryParse((string)lat.GetValue(settings), NumberStyles.Float, CultureInfo.InvariantCulture, out a) ||
                    !double.TryParse((string)lon.GetValue(settings), NumberStyles.Float, CultureInfo.InvariantCulture, out b) ||
                    double.IsNaN(a) || double.IsInfinity(a) || double.IsNaN(b) || double.IsInfinity(b) || Math.Abs(a) > 90 || Math.Abs(b) > 180)
                { lat.SetValue(settings, ""); lon.SetValue(settings, ""); }
            }
#if !SUITE_CORE_TEST
            if (fields.Where((f, i) => !object.Equals(original[i], f.GetValue(settings))).Any()) RegistryFiles.PreserveInvalidSettings();
#endif
        }
    }

#if !SUITE_CORE_TEST
    public static class RegistryFiles
    {
        public static bool Run(string arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            try
            {
                using (var p = System.Diagnostics.Process.Start(start))
                {
                    p.OutputDataReceived += (s, e) => { }; p.ErrorDataReceived += (s, e) => { };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); p.WaitForExit(); } catch { } return false; }
                    p.WaitForExit(); return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }
        static bool ValidId(string id) { return Regex.IsMatch(id, @"^Kinetik(?:Calendar|Clock|Dock|Feeds|Focus|Hub|Notes|Screen|Weather)?$"); }
        public static bool Replace(string file, IEnumerable<string> ids)
        {
            var keys = ids.Distinct().ToArray();
            if (keys.Length == 0 || keys.Any(id => !ValidId(id))) return false;
            // Snapshot the validated content to eliminate the validate/reopen gap.
            var bytes = File.ReadAllBytes(file);
            var text = Encoding.Unicode.GetString(bytes);
            using (var reader = new StreamReader(new MemoryStream(bytes), true)) text = reader.ReadToEnd();
            if (!text.StartsWith("Windows Registry Editor Version 5.00", StringComparison.Ordinal)) return false;
            foreach (var line in text.Split('\n'))
            {
                var l = line.Trim(); if (!l.StartsWith("[")) continue;
                if (!keys.Any(id => l.Equals("[HKEY_CURRENT_USER\\Software\\" + id + "]", StringComparison.OrdinalIgnoreCase) || l.StartsWith("[HKEY_CURRENT_USER\\Software\\" + id + "\\", StringComparison.OrdinalIgnoreCase))) return false;
            }
            var recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kinetik", "Recovery", "registry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(recovery);
            var incoming = Path.Combine(recovery, "incoming.reg"); File.WriteAllBytes(incoming, bytes);
            var existed = new List<string>();
            foreach (var id in keys)
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\" + id)) if (key == null) continue;
                if (!Run("export \"HKCU\\Software\\" + id + "\" \"" + Path.Combine(recovery, id + ".reg") + "\" /y")) throw new IOException("Could not back up existing settings; nothing was replaced.");
                existed.Add(id);
            }
            try
            {
                foreach (var id in keys) Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\" + id, false);
                if (!Run("import \"" + incoming + "\"")) throw new IOException("Registry import failed.");
                return true;
            }
            catch (Exception failure)
            {
                bool restored = true;
                foreach (var id in keys)
                {
                    try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\" + id, false); } catch { restored = false; }
                    if (existed.Contains(id) && !Run("import \"" + Path.Combine(recovery, id + ".reg") + "\"")) restored = false;
                }
                throw new IOException(failure.Message + (restored ? " Previous settings were restored. " : " Automatic recovery was incomplete. ") + "Recovery files: " + recovery, failure);
            }
        }
        public static bool CannotSave;
        static bool preserved;
        public static void PreserveInvalidSettings()
        {
            if (preserved) return;
            var assembly = Assembly.GetExecutingAssembly();
            var program = assembly.GetType("Program"); var demo = program == null ? null : program.GetField("Demo");
            if (demo != null && true.Equals(demo.GetValue(null))) return;
            var id = assembly.GetName().Name.Replace(".new", "").Replace(".check", "");
            if (!ValidId(id)) return;
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\" + id)) if (key == null) return;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kinetik", "Recovery");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, id + "-invalid-" + Guid.NewGuid().ToString("N") + ".reg");
            if (!Run("export \"HKCU\\Software\\" + id + "\" \"" + file + "\" /y"))
            {
                var settings = assembly.GetType("Settings"); var guard = settings == null ? null : settings.GetField("Unreadable");
                if (guard != null) guard.SetValue(null, true);
                CannotSave = true; preserved = true;
                System.Windows.Forms.MessageBox.Show("Invalid settings could not be backed up. Saving is disabled until the app restarts.", id);
                return;
            }
            preserved = true;
            System.Windows.Forms.MessageBox.Show("Some saved settings were outside the supported range and have been corrected. The original settings were kept at:\n" + file, id);
        }
    }

    public sealed class Shutdown : IDisposable
    {
        readonly EventWaitHandle signal;
        readonly System.Windows.Forms.Timer timer;
        public Shutdown(string id, Action close)
        {
            var security = new System.Security.AccessControl.EventWaitHandleSecurity();
            // No integrity label ("S:(ML…)"): setting one needs a privilege apps don't hold, and the event gets this process's level anyway.
            security.SetSecurityDescriptorSddlForm("D:(A;;0x1F0003;;;SY)(A;;0x1F0003;;;BA)(A;;0x1F0003;;;" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value + ")");
            bool created;
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + id + ".Shutdown", out created, security);
            timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += (s, e) => { if (signal.WaitOne(0)) { timer.Stop(); close(); } };
            timer.Start();
        }
        public static bool Request(string id)
        { try { using (var e = EventWaitHandle.OpenExisting(@"Local\" + id + ".Shutdown")) e.Set(); return true; } catch { return false; } }
        public void Dispose() { timer.Dispose(); signal.Dispose(); }
    }

    // Right-click menus close when you click anywhere else, the desktop included. The widgets never take focus, so
    // without this Windows doesn't count that click as leaving the app and the menu stays open.
    public static class MenuFocus
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        public static T Attach<T>(T menu) where T : System.Windows.Forms.ToolStripDropDown
        {
            menu.Opened += (s, e) => { try { SetForegroundWindow(menu.Handle); } catch { } };
            return menu;
        }
    }

    public static class MemoryPolicy
    {
        // Preserve existing behaviour until Windows measurements justify a change.
        // Opt-in CSV timing makes the proposed benchmark reproducible without claiming gains.
        public static void Measure(string app, Action trim)
        {
            if (Environment.GetEnvironmentVariable("KINETIK_SKIP_TRIM") == "1") return;
            if (Environment.GetEnvironmentVariable("KINETIK_MEASURE_TRIM") != "1") { trim(); return; }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            long before = GC.GetTotalMemory(false); trim(); watch.Stop();
            try
            {
                bool admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                var dir = Path.Combine(Environment.GetFolderPath(admin ? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.LocalApplicationData), "Kinetik", "Diagnostics"); Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "memory-trim.csv"), DateTime.UtcNow.ToString("o") + "," + app + "," + before + "," + GC.GetTotalMemory(false) + "," + watch.Elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + "\r\n");
            }
            catch { }
        }
    }
#endif
}
