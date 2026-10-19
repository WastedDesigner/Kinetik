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
        static HttpWebResponse Open(HttpWebRequest request)
        {
            try { return (HttpWebResponse)request.GetResponse(); }
            catch (WebException error)
            {
                // Failed HTTP responses also own a connection; callers still retain the status metadata.
                if (error.Response != null) error.Response.Close();
                throw;
            }
        }
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
                using (var response = Open(request))
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
        readonly System.Windows.Forms.Control dispatcher;
        readonly RegisteredWaitHandle wait;
        int disposed;
        public Shutdown(string id, Action close)
        {
            var security = new System.Security.AccessControl.EventWaitHandleSecurity();
            // No integrity label ("S:(ML…)"): setting one needs a privilege apps don't hold, and the event gets this process's level anyway.
            security.SetSecurityDescriptorSddlForm("D:(A;;0x1F0003;;;SY)(A;;0x1F0003;;;BA)(A;;0x1F0003;;;" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value + ")");
            bool created;
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + id + ".Shutdown", out created, security);
            // A registered wait sleeps until requested instead of waking every widget four times a second.
            dispatcher = new System.Windows.Forms.Control();
            dispatcher.CreateControl();
            wait = ThreadPool.RegisterWaitForSingleObject(signal, (state, timedOut) =>
            {
                if (Interlocked.CompareExchange(ref disposed, 0, 0) != 0) return;
                Ui.Post(dispatcher, () => { if (Interlocked.CompareExchange(ref disposed, 0, 0) == 0) close(); });
            }, null, Timeout.Infinite, true);
        }
        public static bool Request(string id)
        { try { using (var e = EventWaitHandle.OpenExisting(@"Local\" + id + ".Shutdown")) e.Set(); return true; } catch { return false; } }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            wait.Unregister(null); dispatcher.Dispose(); signal.Dispose();
        }
    }

    public static class Ui
    {
        // System events and worker completions can arrive after their window has closed.
        public static bool Post(System.Windows.Forms.Control control, Action action)
        {
            if (control.IsDisposed || control.Disposing || !control.IsHandleCreated) return false;
            try
            {
                control.BeginInvoke((Action)(() => { if (!control.IsDisposed && !control.Disposing) action(); }));
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
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

    // How every Kinetik app's own windows, menus and dialogs look: a theme and an accent colour picked once (in
    // Kinetik or the Hub) for the whole suite. Widget themes are separate; this is only the app around them.
    public sealed class Look
    {
        public string Name;
        public bool Light;
        // Page, sidebar, card, card under the mouse, text box, hairline, text, secondary text, accent, text on the accent, warning, good.
        public System.Drawing.Color Bg, Nav, Card, Hover, Field, Border, Text, Sub, Accent, OnAccent, Warn, Good;

        public const string Key = @"Software\Kinetik Suite";
        public const string Auto = "Match Windows", WindowsAccent = "Windows accent";
        public static readonly string[] Themes = { Auto, "Graphite", "OLED black", "Midnight", "Nord", "Mocha", "Forest", "Light", "Paper" };
        public static readonly string[] Accents = { "Sky", "Violet", "Mint", "Amber", "Rose", "Coral", WindowsAccent };

        static System.Drawing.Color C(int r, int g, int b) { return System.Drawing.Color.FromArgb(r, g, b); }

        // name → page, sidebar, card, hover, field, border, text, secondary text
        static readonly Dictionary<string, System.Drawing.Color[]> palettes = new Dictionary<string, System.Drawing.Color[]>
        {
            { "Graphite", new[] { C(17, 17, 19), C(17, 17, 19), C(26, 26, 29), C(34, 34, 38), C(31, 31, 35), C(44, 44, 50), C(236, 236, 240), C(142, 142, 152) } },
            { "OLED black", new[] { C(0, 0, 0), C(0, 0, 0), C(14, 14, 16), C(24, 24, 27), C(20, 20, 23), C(36, 36, 40), C(242, 242, 245), C(136, 136, 146) } },
            { "Midnight", new[] { C(13, 18, 32), C(13, 18, 32), C(21, 28, 46), C(28, 37, 59), C(25, 33, 53), C(39, 50, 76), C(230, 235, 245), C(135, 146, 171) } },
            { "Nord", new[] { C(46, 52, 64), C(46, 52, 64), C(54, 61, 75), C(61, 69, 85), C(58, 65, 80), C(70, 79, 97), C(236, 239, 244), C(163, 171, 186) } },
            { "Mocha", new[] { C(30, 30, 46), C(30, 30, 46), C(39, 39, 57), C(49, 50, 69), C(45, 46, 64), C(60, 61, 82), C(205, 214, 244), C(147, 153, 178) } },
            { "Forest", new[] { C(16, 24, 20), C(16, 24, 20), C(24, 34, 29), C(31, 44, 37), C(28, 39, 33), C(42, 58, 49), C(228, 240, 231), C(138, 163, 147) } },
            { "Light", new[] { C(246, 246, 248), C(246, 246, 248), C(255, 255, 255), C(240, 240, 244), C(255, 255, 255), C(222, 222, 229), C(27, 27, 31), C(104, 104, 116) } },
            { "Paper", new[] { C(247, 244, 238), C(247, 244, 238), C(253, 251, 247), C(242, 237, 228), C(255, 255, 255), C(226, 219, 206), C(46, 38, 30), C(120, 106, 90) } },
        };

        // name → colour on dark themes, colour on light themes
        static readonly Dictionary<string, System.Drawing.Color[]> accents = new Dictionary<string, System.Drawing.Color[]>
        {
            { "Sky", new[] { C(76, 194, 255), C(0, 110, 200) } },
            { "Violet", new[] { C(167, 139, 250), C(110, 70, 230) } },
            { "Mint", new[] { C(52, 211, 153), C(10, 140, 95) } },
            { "Amber", new[] { C(251, 191, 36), C(176, 112, 0) } },
            { "Rose", new[] { C(244, 114, 182), C(196, 40, 120) } },
            { "Coral", new[] { C(255, 128, 102), C(210, 70, 45) } },
        };

        static Look current;
        static int readAt;
        // Re-read every couple of seconds at most, so a theme picked in another Kinetik app shows up here too.
        public static Look Current
        {
            get
            {
                if (current == null || unchecked(Environment.TickCount - readAt) > 2000) Reload();
                return current;
            }
        }

        public static Look Reload() { current = Read(); readAt = Environment.TickCount; return current; }

        public static string SavedTheme { get { return Value("Theme", Auto); } }
        public static string SavedAccent { get { return Value("Accent", "Sky"); } }
        public static bool Saved { get { return Value("Theme", null) != null; } }

        static string Value(string name, string fallback)
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k == null ? fallback : k.GetValue(name, fallback) as string ?? fallback; }
            catch { return fallback; }
        }

        public static void Save(string theme, string accent)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (theme != null) k.SetValue("Theme", theme);
                    if (accent != null) k.SetValue("Accent", accent);
                }
            }
            catch { }
            Reload();
        }

        static Look Read() { return Make(SavedTheme, SavedAccent); }

        public static bool WindowsIsLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1;
            }
            catch { return false; }
        }

        public static Look Make(string theme, string accent)
        {
            var name = theme == Auto ? (WindowsIsLight() ? "Light" : "Graphite") : theme;
            System.Drawing.Color[] p;
            if (!palettes.TryGetValue(name ?? "", out p)) { name = "Graphite"; p = palettes[name]; }
            var l = new Look { Name = theme == Auto ? Auto : name, Light = name == "Light" || name == "Paper" };
            l.Bg = p[0]; l.Nav = p[1]; l.Card = p[2]; l.Hover = p[3]; l.Field = p[4]; l.Border = p[5]; l.Text = p[6]; l.Sub = p[7];
            l.Accent = AccentColour(accent, l.Light);
            l.OnAccent = Luma(l.Accent) > 0.55 ? C(17, 17, 19) : C(255, 255, 255);
            l.Warn = l.Light ? C(200, 40, 40) : C(255, 110, 110);
            l.Good = l.Light ? C(20, 140, 70) : C(110, 210, 140);
            return l;
        }

        public static System.Drawing.Color AccentColour(string accent, bool light)
        {
            System.Drawing.Color[] a;
            if (accent == WindowsAccent)
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        var v = k == null ? null : k.GetValue("AccentColor");
                        if (v != null)
                        {
                            int abgr = Convert.ToInt32(v);
                            var c = C(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                            // Lift a dark accent on dark themes and deepen a pale one on light themes, so it stays readable.
                            double y = Luma(c);
                            if (!light && y < 0.45) c = Mix(c, C(255, 255, 255), (float)Math.Min(0.6, 0.6 - y));
                            if (light && y > 0.5) c = Mix(c, C(0, 0, 0), (float)Math.Min(0.5, y - 0.35));
                            return c;
                        }
                    }
                }
                catch { }
                accent = "Sky";
            }
            if (!accents.TryGetValue(accent ?? "", out a)) a = accents["Sky"];
            return light ? a[1] : a[0];
        }

        public static double Luma(System.Drawing.Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }

        public static System.Drawing.Color Mix(System.Drawing.Color a, System.Drawing.Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return System.Drawing.Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        static int Ref(System.Drawing.Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Dark or light title bar in the page's colour, with a hairline border and rounded corners. Older Windows ignores what it doesn't know.
        public void Window(IntPtr handle)
        {
            try
            {
                int dark = Light ? 0 : 1; DwmSetWindowAttribute(handle, 20, ref dark, 4);
                int round = 2; DwmSetWindowAttribute(handle, 33, ref round, 4);
                int caption = Ref(Bg); DwmSetWindowAttribute(handle, 35, ref caption, 4);
                int text = Ref(Text); DwmSetWindowAttribute(handle, 36, ref text, 4);
                int border = Ref(Border); DwmSetWindowAttribute(handle, 34, ref border, 4);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

        // Dark scroll bars on a scrolling panel or list in a dark theme. Call once its handle exists.
        public void ScrollBars(System.Windows.Forms.Control control)
        {
            System.Windows.Forms.MethodInvoker apply = () => { try { SetWindowTheme(control.Handle, Light ? "Explorer" : "DarkMode_Explorer", null); } catch { } };
            if (control.IsHandleCreated) apply(); else control.HandleCreated += (s, e) => apply();
        }

        // "App theme" for any app's right-click menu: every theme and accent, ticked as chosen. Picking one changes every Kinetik app.
        public static System.Windows.Forms.ToolStripMenuItem MenuItem(Action changed)
        {
            var menu = new System.Windows.Forms.ToolStripMenuItem("App theme");
            menu.DropDownOpening += (s, e) => { menu.DropDownItems.Clear(); FillMenu(menu.DropDownItems, changed); };
            menu.DropDownItems.Add("…"); // replaced as it opens
            return menu;
        }

        // Repaints an open window after the theme changes: every control coloured like the old theme takes the new one's colour.
        public static void Recolour(System.Windows.Forms.Control root, Look old)
        {
            var now = Current;
            var from = new[] { old.Bg, old.Nav, old.Card, old.Hover, old.Field, old.Border, old.Text, old.Sub, old.Accent, old.OnAccent };
            var to = new[] { now.Bg, now.Nav, now.Card, now.Hover, now.Field, now.Border, now.Text, now.Sub, now.Accent, now.OnAccent };
            Func<System.Drawing.Color, System.Drawing.Color> map = c => { for (int i = 0; i < from.Length; i++) if (from[i].ToArgb() == c.ToArgb()) return to[i]; return c; };
            Action<System.Windows.Forms.Control> walk = null;
            walk = c =>
            {
                if (c.BackColor.A == 255) c.BackColor = map(c.BackColor);
                c.ForeColor = map(c.ForeColor);
                foreach (System.Windows.Forms.Control k in c.Controls) walk(k);
                c.Invalidate();
            };
            walk(root);
            var form = root as System.Windows.Forms.Form;
            if (form != null && form.IsHandleCreated) now.Window(form.Handle);
        }

        // The themes, then the accents, as menu items ticked as chosen.
        public static void FillMenu(System.Windows.Forms.ToolStripItemCollection items, Action changed)
        {
            {
                string theme = SavedTheme, accent = SavedAccent;
                foreach (var t in Themes)
                {
                    var name = t;
                    var it = new System.Windows.Forms.ToolStripMenuItem(name) { Checked = name == theme };
                    it.Click += (o, a) => { Save(name, null); if (changed != null) changed(); };
                    items.Add(it);
                }
                items.Add(new System.Windows.Forms.ToolStripSeparator());
                foreach (var c in Accents)
                {
                    var name = c;
                    var it = new System.Windows.Forms.ToolStripMenuItem(name == WindowsAccent ? "Accent: Windows accent colour" : "Accent: " + name) { Checked = name == accent };
                    it.Click += (o, a) => { Save(null, name); if (changed != null) changed(); };
                    items.Add(it);
                }
            }
        }

        // Right-click menus in the same colours.
        public System.Windows.Forms.ToolStripRenderer MenuRenderer() { return new LookMenu(); }
    }

    sealed class LookMenu : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        static Look look { get { return Look.Current; } }
        public LookMenu() : base(new LookColours()) { RoundedEdges = false; }
        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? look.Text : look.Sub;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(System.Windows.Forms.ToolStripArrowRenderEventArgs e) { e.ArrowColor = look.Sub; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(System.Windows.Forms.ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics; var r = e.ImageRectangle;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var p = new System.Drawing.Pen(look.Accent, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawLines(p, new[] { new System.Drawing.PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.52f), new System.Drawing.PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f), new System.Drawing.PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.3f) });
        }
    }

    sealed class LookColours : System.Windows.Forms.ProfessionalColorTable
    {
        static Look l { get { return Look.Current; } }
        public LookColours() { UseSystemColors = false; }
        public override System.Drawing.Color ToolStripDropDownBackground { get { return l.Card; } }
        public override System.Drawing.Color ImageMarginGradientBegin { get { return l.Card; } }
        public override System.Drawing.Color ImageMarginGradientMiddle { get { return l.Card; } }
        public override System.Drawing.Color ImageMarginGradientEnd { get { return l.Card; } }
        public override System.Drawing.Color MenuBorder { get { return l.Border; } }
        public override System.Drawing.Color MenuItemBorder { get { return l.Hover; } }
        public override System.Drawing.Color MenuItemSelected { get { return l.Hover; } }
        public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return l.Hover; } }
        public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return l.Hover; } }
        public override System.Drawing.Color MenuItemPressedGradientBegin { get { return l.Hover; } }
        public override System.Drawing.Color MenuItemPressedGradientEnd { get { return l.Hover; } }
        public override System.Drawing.Color SeparatorDark { get { return l.Border; } }
        public override System.Drawing.Color SeparatorLight { get { return l.Card; } }
        public override System.Drawing.Color CheckBackground { get { return l.Card; } }
        public override System.Drawing.Color CheckSelectedBackground { get { return l.Hover; } }
        public override System.Drawing.Color CheckPressedBackground { get { return l.Hover; } }
    }

    // Controls in the Kinetik apps' theme, for settings windows and dialogs, in place of Windows' own checkbox,
    // button and drop-down, which stay light whatever the theme.
    static class Paths
    {
        public static System.Drawing.Drawing2D.GraphicsPath Round(System.Drawing.RectangleF r, float rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath(); float d = Math.Max(0.5f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void Fill(System.Drawing.Graphics g, System.Drawing.RectangleF r, float rad, System.Drawing.Color c)
        { using (var p = Round(r, rad)) using (var b = new System.Drawing.SolidBrush(c)) g.FillPath(b, p); }
        public static void Stroke(System.Drawing.Graphics g, System.Drawing.RectangleF r, float rad, System.Drawing.Color c)
        { using (var p = Round(r, rad)) using (var pen = new System.Drawing.Pen(c, 1)) g.DrawPath(pen, p); }
        public static void Chevron(System.Drawing.Graphics g, System.Drawing.RectangleF r, System.Drawing.Color c, bool up = false)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, w = (up ? -1 : 1) * Math.Min(r.Width, r.Height) * 0.16f;
            using (var p = new System.Drawing.Pen(c, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawLines(p, new[] { new System.Drawing.PointF(cx - w, cy - w / 2), new System.Drawing.PointF(cx, cy + w / 2), new System.Drawing.PointF(cx + w, cy - w / 2) });
        }
        // Corners show the parent's colour.
        public static void Follow(System.Windows.Forms.Control c)
        {
            c.ParentChanged += (s, e) => { if (c.Parent != null) c.BackColor = c.Parent.BackColor; };
        }
    }

    // An on/off switch.
    public class Switch : System.Windows.Forms.Control
    {
        bool on;
        public event EventHandler Changed;
        public bool On { get { return on; } set { if (on == value) return; on = value; Invalidate(); } }
        public Switch(bool on, float scale)
        {
            this.on = on;
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer | System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.Selectable, true);
            Size = new System.Drawing.Size((int)(42 * scale), (int)(22 * scale)); Cursor = System.Windows.Forms.Cursors.Hand; TabStop = true;
            Paths.Follow(this);
        }
        protected override void OnClick(EventArgs e) { On = !On; if (Changed != null) Changed(this, e); base.OnClick(e); }
        protected override void OnKeyDown(System.Windows.Forms.KeyEventArgs e) { if (e.KeyCode == System.Windows.Forms.Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics; var l = Look.Current; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new System.Drawing.RectangleF(1, 1, Width - 3, Height - 3);
            if (on) Paths.Fill(g, r, r.Height / 2, l.Accent);
            else using (var p = Paths.Round(r, r.Height / 2)) using (var pen = new System.Drawing.Pen(Focused && ShowFocusCues ? l.Accent : l.Sub, 1.5f)) g.DrawPath(pen, p);
            float d = r.Height - (on ? 8 : 10), x = on ? r.Right - d - 4 : r.X + 5;
            using (var b = new System.Drawing.SolidBrush(on ? l.OnAccent : l.Sub)) g.FillEllipse(b, x, r.Y + (r.Height - d) / 2, d, d);
        }
    }

    // A rounded button. An accent button is filled with the accent colour.
    public class FlatButton : System.Windows.Forms.Control
    {
        readonly bool accent;
        readonly float scale;
        bool hot, down;
        public FlatButton(string text, bool accent, float scale)
        {
            this.accent = accent; this.scale = scale;
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer | System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.Selectable | System.Windows.Forms.ControlStyles.StandardClick, true);
            Cursor = System.Windows.Forms.Cursors.Hand; TabStop = true;
            Paths.Follow(this);
            Text = text;
        }
        protected override void OnTextChanged(EventArgs e) { Fit(); base.OnTextChanged(e); }
        protected override void OnFontChanged(EventArgs e) { Fit(); base.OnFontChanged(e); }
        void Fit() { Size = new System.Drawing.Size(Math.Max((int)(76 * scale), System.Windows.Forms.TextRenderer.MeasureText(Text, Font).Width + (int)(26 * scale)), (int)(30 * scale)); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnKeyDown(System.Windows.Forms.KeyEventArgs e) { if (e.KeyCode == System.Windows.Forms.Keys.Space || e.KeyCode == System.Windows.Forms.Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics; var l = Look.Current; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new System.Drawing.RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            System.Drawing.Color fill = accent ? l.Accent : l.Field, text = accent ? l.OnAccent : l.Text;
            if (accent && Enabled && (hot || down)) fill = Look.Mix(fill, l.Light ? System.Drawing.Color.Black : System.Drawing.Color.White, down ? 0.05f : 0.14f);
            else if (!accent && Enabled && (hot || down)) fill = down ? l.Border : l.Hover;
            if (!Enabled) { fill = l.Field; text = l.Sub; }
            Paths.Fill(g, r, 6 * scale, fill);
            if (!accent || !Enabled) Paths.Stroke(g, r, 6 * scale, Focused && ShowFocusCues ? l.Accent : l.Border);
            // ↑ and ↓ are drawn as chevrons: the font's arrows can come out as coloured emoji.
            if (Text == "↑" || Text == "↓") Paths.Chevron(g, new System.Drawing.RectangleF(0, 0, Width - 1, Height - 1), text, Text == "↑");
            else System.Windows.Forms.TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.SingleLine | System.Windows.Forms.TextFormatFlags.NoPrefix);
        }
    }

    // A settings row. Rows one after another join into one rounded card split by hairlines, as in Kinetik's settings.
    public class CardRow : System.Windows.Forms.Panel
    {
        bool first = true, last = true;
        readonly float scale;
        public CardRow(float scale)
        {
            this.scale = scale;
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer | System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.ResizeRedraw, true);
            BackColor = Look.Current.Card;
            Margin = new System.Windows.Forms.Padding(0, 0, 0, (int)(8 * scale));
        }
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent == null) return;
            Parent.ControlAdded -= Rejoin; Parent.ControlAdded += Rejoin;
            Parent.ControlRemoved -= Rejoin; Parent.ControlRemoved += Rejoin;
            Rejoin(Parent, null);
        }
        static void Rejoin(object sender, System.Windows.Forms.ControlEventArgs e)
        {
            var kids = ((System.Windows.Forms.Control)sender).Controls;
            for (int i = 0; i < kids.Count; i++)
            {
                var row = kids[i] as CardRow;
                if (row == null) continue;
                bool f = i == 0 || !(kids[i - 1] is CardRow), l = i == kids.Count - 1 || !(kids[i + 1] is CardRow);
                if (f == row.first && l == row.last) continue;
                row.first = f; row.last = l;
                row.Margin = new System.Windows.Forms.Padding(0, 0, 0, l ? (int)(8 * row.scale) : 0);
                row.Invalidate();
            }
        }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics; var lk = Look.Current; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : lk.Bg);
            var r = new System.Drawing.RectangleF(0, 0, Width - 1, last ? Height - 1 : Height);
            float d = 16 * scale;
            using (var p = new System.Drawing.Drawing2D.GraphicsPath())
            {
                if (first) { p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); } else p.AddLine(r.X, r.Y, r.Right, r.Y);
                if (last) { p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); } else p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
                p.CloseFigure();
                using (var b = new System.Drawing.SolidBrush(BackColor)) g.FillPath(b, p);
            }
            if (!first) using (var pen = new System.Drawing.Pen(lk.Border, 1)) g.DrawLine(pen, 16 * scale, 0, Width - 16 * scale, 0);
        }
    }

    // A settings window in the style of Kinetik Stats' own: a sidebar of pages, the page's title and what it's for, and
    // rows of switches, drop-downs and buttons that join into cards. Each app gives it its pages.
    public class SettingsWindow : System.Windows.Forms.Form
    {
        public class Page { public string Name, Sub, Glyph; public Action<Builder> Build; }

        // What a page's Build gets: the calls that add its parts, top to bottom.
        public class Builder
        {
            internal SettingsWindow w;
            internal System.Windows.Forms.FlowLayoutPanel list;
            public float K { get { return w.k; } }
            public int Width { get { return w.RowW; } }
            int S(float v) { return (int)Math.Round(v * w.k); }

            public void Section(string title)
            {
                list.Controls.Add(new System.Windows.Forms.Label { Text = title.ToUpperInvariant(), Font = w.UI(8f, System.Drawing.FontStyle.Bold), ForeColor = Look.Current.Sub, AutoSize = false, Size = new System.Drawing.Size(w.RowW, S(32)), TextAlign = System.Drawing.ContentAlignment.BottomLeft, Margin = new System.Windows.Forms.Padding(S(4), S(10), 0, S(8)) });
            }

            public void Hint(string text, int lines = 2)
            {
                list.Controls.Add(new System.Windows.Forms.Label { Text = text, Font = w.UI(9f), ForeColor = Look.Current.Sub, AutoSize = false, Size = new System.Drawing.Size(w.RowW, S(18 * lines + 6)), Margin = new System.Windows.Forms.Padding(S(4), S(2), 0, S(8)) });
            }

            // A row: title, an optional line under it, and a control on the right.
            public void Row(string title, string desc, System.Windows.Forms.Control right)
            {
                var row = new CardRow(w.k) { Size = new System.Drawing.Size(w.RowW, S(desc == null ? 50 : 60)) };
                var l = Look.Current;
                int rw = right == null ? 0 : right.Width;
                row.Controls.Add(new System.Windows.Forms.Label { Text = title, Font = w.UI(10f), ForeColor = l.Text, BackColor = l.Card, AutoSize = false, AutoEllipsis = true, Size = new System.Drawing.Size(w.RowW - rw - S(44), S(22)), Location = new System.Drawing.Point(S(16), desc == null ? (row.Height - S(22)) / 2 : S(9)) });
                if (desc != null) row.Controls.Add(new System.Windows.Forms.Label { Text = desc, Font = w.UI(8.5f), ForeColor = l.Sub, BackColor = l.Card, AutoSize = false, AutoEllipsis = true, Size = new System.Drawing.Size(w.RowW - rw - S(44), S(18)), Location = new System.Drawing.Point(S(16), S(32)) });
                if (right != null)
                {
                    right.Location = new System.Drawing.Point(w.RowW - right.Width - S(16), (row.Height - right.Height) / 2);
                    row.Controls.Add(right);
                }
                list.Controls.Add(row);
            }

            public void Switch(string title, string desc, bool value, Action<bool> set)
            {
                var sw = new KinetikSuite.Switch(value, w.k);
                sw.Changed += (s, e) => set(sw.On);
                Row(title, desc, sw);
            }

            public void Choice(string title, string desc, string[] items, int selected, Action<int> set, int width = 200)
            {
                var cb = new Dropdown { Width = S(width), Font = w.UI(9.5f), ItemHeight = S(22) };
                cb.Items.AddRange(items);
                cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
                cb.SelectedIndexChanged += (s, e) => set(cb.SelectedIndex);
                Row(title, desc, cb);
            }

            public FlatButton Button(string text, Action click, bool accent = false)
            {
                var b = new FlatButton(text, accent, w.k) { Font = w.UI(9.5f) };
                if (click != null) b.Click += (s, e) => click();
                return b;
            }

            // A small square button, for arrows and the like.
            public FlatButton Small(string text, Action click)
            {
                var btn = Button(text, click);
                btn.Width = S(34);
                return btn;
            }

            // A row with several buttons on the right.
            public void Buttons(string title, string desc, params FlatButton[] buttons)
            {
                var flow = new System.Windows.Forms.FlowLayoutPanel { AutoSize = true, AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Look.Current.Card, Margin = System.Windows.Forms.Padding.Empty, Padding = System.Windows.Forms.Padding.Empty };
                foreach (var b in buttons) { b.Margin = new System.Windows.Forms.Padding(S(6), 0, 0, 0); flow.Controls.Add(b); }
                flow.Size = flow.PreferredSize;
                Row(title, desc, flow);
            }

            public void Add(System.Windows.Forms.Control control) { list.Controls.Add(control); }

            // Builds the page again, keeping where it was scrolled to: after a change that adds or removes rows.
            public void Refresh() { w.BeginInvoke((Action)(() => w.ShowPage(w.page, true))); }
        }

        readonly List<Page> pages;
        readonly System.Windows.Forms.Panel nav = new System.Windows.Forms.Panel(), header = new System.Windows.Forms.Panel(), content = new System.Windows.Forms.Panel();
        readonly List<System.Windows.Forms.Control> navItems = new List<System.Windows.Forms.Control>();
        readonly string app;
        readonly System.Drawing.Image logo;
        System.Windows.Forms.FlowLayoutPanel list;
        Page page;
        float k = 1;
        internal int RowW { get { return content.ClientSize.Width - (int)(56 * k) - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth; } }

        readonly Dictionary<string, System.Drawing.Font> fonts = new Dictionary<string, System.Drawing.Font>();
        internal System.Drawing.Font UI(float pt, System.Drawing.FontStyle st = System.Drawing.FontStyle.Regular)
        {
            var key = pt + "|" + st;
            System.Drawing.Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                string family = "Segoe UI";
                try { using (var t = new System.Drawing.Font("Segoe UI Variable Text", 9f)) if (t.Name == "Segoe UI Variable Text") family = t.Name; } catch { }
                fonts[key] = f = new System.Drawing.Font(family, pt, st);
            }
            return f;
        }

        public SettingsWindow(string app, string version, List<Page> pages, string first)
        {
            this.app = app; this.pages = pages;
            var l = Look.Reload();
            Text = app + " settings";
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); logo = Icon.ToBitmap(); } catch { }
            k = DeviceDpi / 96f;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(S(800), S(620));
            MinimumSize = new System.Drawing.Size(S(700), S(460));
            BackColor = l.Bg; ForeColor = l.Text; Font = UI(9.5f);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == System.Windows.Forms.Keys.Escape) Close(); };

            // sidebar: the app's icon and name, then its pages
            nav.Dock = System.Windows.Forms.DockStyle.Left; nav.Width = S(208); nav.BackColor = l.Nav; nav.Padding = new System.Windows.Forms.Padding(0, S(76), 1, 0);
            nav.Paint += (s, e) =>
            {
                var g = e.Graphics; var lk = Look.Current;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                if (logo != null) g.DrawImage(logo, new System.Drawing.Rectangle(S(20), S(24), S(26), S(26)));
                string top = app.StartsWith("Kinetik ") ? "Kinetik" : app, second = app.StartsWith("Kinetik ") ? app.Substring(8) : "";
                System.Windows.Forms.TextRenderer.DrawText(g, top, UI(12.5f, System.Drawing.FontStyle.Bold), new System.Drawing.Point(S(54), S(18)), lk.Text, System.Windows.Forms.TextFormatFlags.NoPadding);
                if (second != "") System.Windows.Forms.TextRenderer.DrawText(g, second, UI(9f), new System.Drawing.Point(S(55), S(40)), lk.Sub, System.Windows.Forms.TextFormatFlags.NoPadding);
                System.Windows.Forms.TextRenderer.DrawText(g, "v" + version, UI(8.5f), new System.Drawing.Point(S(22), nav.Height - S(32)), lk.Sub, System.Windows.Forms.TextFormatFlags.NoPadding);
                using (var pen = new System.Drawing.Pen(lk.Border, 1)) g.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height);
            };
            var navList = new System.Windows.Forms.FlowLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, FlowDirection = System.Windows.Forms.FlowDirection.TopDown, WrapContents = false, BackColor = l.Nav, Padding = new System.Windows.Forms.Padding(S(10), 0, S(10), 0) };
            foreach (var p in pages) navList.Controls.Add(NavItem(p));
            nav.Controls.Add(navList);

            // the page's title and what it's for
            header.Dock = System.Windows.Forms.DockStyle.Top; header.Height = S(82); header.BackColor = l.Bg;
            header.Paint += (s, e) =>
            {
                var g = e.Graphics; var lk = Look.Current; g.Clear(lk.Bg);
                if (page == null) return;
                System.Windows.Forms.TextRenderer.DrawText(g, page.Name, UI(17f, System.Drawing.FontStyle.Bold), new System.Drawing.Point(S(28), S(20)), lk.Text, System.Windows.Forms.TextFormatFlags.NoPadding);
                System.Windows.Forms.TextRenderer.DrawText(g, page.Sub ?? "", UI(9.5f), new System.Drawing.Rectangle(S(29), S(54), header.Width - S(56), S(20)), lk.Sub, System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.EndEllipsis | System.Windows.Forms.TextFormatFlags.SingleLine);
            };
            content.Dock = System.Windows.Forms.DockStyle.Fill; content.BackColor = l.Bg;
            var right = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill, BackColor = l.Bg };
            right.Controls.Add(content); right.Controls.Add(header);
            Controls.Add(right); Controls.Add(nav);
            Resize += (s, e) => { if (page != null && IsHandleCreated) ShowPage(page, true); };
            // Settings changed from the widget's own menu while this is open show up when it's back in front.
            Activated += (s, e) => { if (page != null && !(ActiveControl is System.Windows.Forms.TextBox)) ShowPage(page, true); };
            // The first page is built once the window has its size, so its rows fit.
            Load += (s, e) => { PerformLayout(); ShowPage(pages.FirstOrDefault(p => p.Name == first) ?? pages[0], false); };
        }

        int S(float v) { return (int)Math.Round(v * k); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Look.Current.Window(Handle); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var f in fonts.Values) f.Dispose(); fonts.Clear(); if (logo != null) logo.Dispose(); }
            base.Dispose(disposing);
        }

        public void ShowPage(string name) { var p = pages.FirstOrDefault(x => x.Name == name); if (p != null) ShowPage(p, false); }

        int lastBuilt;
        internal void ShowPage(Page p, bool keepScroll)
        {
            // Activated and Resize can both ask within a moment; once is enough.
            if (keepScroll && p == page && unchecked(Environment.TickCount - lastBuilt) < 300) return;
            lastBuilt = Environment.TickCount;
            int scroll = keepScroll && list != null && p == page ? -list.AutoScrollPosition.Y : 0;
            page = p;
            foreach (var n in navItems) n.Invalidate();
            header.Invalidate();
            content.SuspendLayout();
            foreach (System.Windows.Forms.Control c in content.Controls.Cast<System.Windows.Forms.Control>().ToList()) c.Dispose();
            list = new System.Windows.Forms.FlowLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, FlowDirection = System.Windows.Forms.FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Look.Current.Bg, Padding = new System.Windows.Forms.Padding(S(24), 0, S(16), S(24)) };
            content.Controls.Add(list);
            Look.Current.ScrollBars(list);
            try { p.Build(new Builder { w = this, list = list }); }
            catch (Exception ex) { list.Controls.Add(new System.Windows.Forms.Label { Text = "This page couldn't be shown: " + ex.Message, ForeColor = Look.Current.Warn, AutoSize = true }); }
            content.ResumeLayout();
            if (scroll > 0) list.AutoScrollPosition = new System.Drawing.Point(0, scroll);
            ActiveControl = null;
        }

        System.Windows.Forms.Control NavItem(Page p)
        {
            var c = new System.Windows.Forms.Label { AutoSize = false, Size = new System.Drawing.Size(S(188), S(36)), Margin = new System.Windows.Forms.Padding(0, 0, 0, S(2)), Cursor = System.Windows.Forms.Cursors.Hand, BackColor = Look.Current.Nav };
            bool hot = false;
            c.MouseEnter += (s, e) => { hot = true; c.Invalidate(); };
            c.MouseLeave += (s, e) => { hot = false; c.Invalidate(); };
            c.Paint += (s, e) =>
            {
                var g = e.Graphics; var l = Look.Current; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(l.Nav);
                bool sel = p == page;
                if (sel || hot) Paths.Fill(g, new System.Drawing.RectangleF(0, 0, c.Width - 1, c.Height - 1), S(6), sel ? l.Hover : l.Card);
                if (sel) Paths.Fill(g, new System.Drawing.RectangleF(0, S(10), S(3), c.Height - S(20)), S(1.5f), l.Accent);
                Glyph(g, p.Glyph, new System.Drawing.RectangleF(S(13), (c.Height - S(16)) / 2f, S(16), S(16)), sel ? l.Accent : hot ? l.Text : l.Sub);
                System.Windows.Forms.TextRenderer.DrawText(g, p.Name, UI(9.75f), new System.Drawing.Rectangle(S(40), 0, c.Width - S(44), c.Height), sel || hot ? l.Text : l.Sub,
                    System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.SingleLine | System.Windows.Forms.TextFormatFlags.NoPrefix | System.Windows.Forms.TextFormatFlags.NoPadding);
            };
            c.Click += (s, e) => ShowPage(p, false);
            navItems.Add(c);
            return c;
        }

        // Small line icons for the sidebar.
        public static void Glyph(System.Drawing.Graphics g, string name, System.Drawing.RectangleF r, System.Drawing.Color c)
        {
            float x = r.X, y = r.Y, s = r.Width;
            Func<float, float, System.Drawing.PointF> P = (a, b) => new System.Drawing.PointF(x + a * s, y + b * s);
            using (var p = new System.Drawing.Pen(c, Math.Max(1.4f, s / 10)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
            using (var b = new System.Drawing.SolidBrush(c))
                switch (name)
                {
                    case "look": // a palette
                        g.DrawEllipse(p, x + 0.08f * s, y + 0.1f * s, 0.84f * s, 0.8f * s);
                        foreach (var d in new[] { P(0.32f, 0.35f), P(0.55f, 0.28f), P(0.7f, 0.48f) }) g.FillEllipse(b, d.X - 0.07f * s, d.Y - 0.07f * s, 0.14f * s, 0.14f * s);
                        break;
                    case "place": // a window on a screen
                        g.DrawRectangle(p, x + 0.08f * s, y + 0.12f * s, 0.84f * s, 0.62f * s);
                        g.DrawLine(p, P(0.35f, 0.9f), P(0.65f, 0.9f));
                        g.FillRectangle(b, x + 0.2f * s, y + 0.24f * s, 0.32f * s, 0.24f * s);
                        break;
                    case "general": // a gear
                        g.DrawEllipse(p, x + 0.3f * s, y + 0.3f * s, 0.4f * s, 0.4f * s);
                        for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(p, x + s / 2 + (float)Math.Cos(a) * 0.3f * s, y + s / 2 + (float)Math.Sin(a) * 0.3f * s, x + s / 2 + (float)Math.Cos(a) * 0.44f * s, y + s / 2 + (float)Math.Sin(a) * 0.44f * s); }
                        break;
                    case "clock":
                        g.DrawEllipse(p, x + 0.08f * s, y + 0.08f * s, 0.84f * s, 0.84f * s);
                        g.DrawLines(p, new[] { P(0.5f, 0.26f), P(0.5f, 0.5f), P(0.68f, 0.62f) });
                        break;
                    case "timer":
                        g.DrawEllipse(p, x + 0.12f * s, y + 0.2f * s, 0.76f * s, 0.76f * s);
                        g.DrawLine(p, P(0.38f, 0.06f), P(0.62f, 0.06f)); g.DrawLine(p, P(0.5f, 0.58f), P(0.5f, 0.38f));
                        break;
                    case "cloud":
                        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                        {
                            path.AddArc(x + 0.06f * s, y + 0.42f * s, 0.36f * s, 0.36f * s, 90, 180);
                            path.AddArc(x + 0.24f * s, y + 0.2f * s, 0.46f * s, 0.46f * s, 180, 160);
                            path.AddArc(x + 0.58f * s, y + 0.4f * s, 0.36f * s, 0.38f * s, 270, 180);
                            path.CloseFigure(); g.DrawPath(p, path);
                        }
                        break;
                    case "places": // a map pin
                        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                        {
                            path.AddArc(x + 0.2f * s, y + 0.06f * s, 0.6f * s, 0.6f * s, 150, 240);
                            path.AddLine(P(0.76f, 0.52f), P(0.5f, 0.94f)); path.AddLine(P(0.5f, 0.94f), P(0.24f, 0.52f));
                            g.DrawPath(p, path);
                        }
                        g.DrawEllipse(p, x + 0.4f * s, y + 0.26f * s, 0.2f * s, 0.2f * s);
                        break;
                    case "dock": // four tiles
                        foreach (var o in new[] { P(0.1f, 0.1f), P(0.56f, 0.1f), P(0.1f, 0.56f), P(0.56f, 0.56f) }) g.DrawRectangle(p, o.X, o.Y, 0.34f * s, 0.34f * s);
                        break;
                    case "list":
                        for (int i = 0; i < 3; i++) { float yy = 0.24f + 0.26f * i; g.FillEllipse(b, x + 0.08f * s, y + (yy - 0.06f) * s, 0.12f * s, 0.12f * s); g.DrawLine(p, P(0.32f, yy), P(0.92f, yy)); }
                        break;
                    default: // about: an i in a circle
                        g.DrawEllipse(p, x + 0.08f * s, y + 0.08f * s, 0.84f * s, 0.84f * s);
                        g.DrawLine(p, P(0.5f, 0.46f), P(0.5f, 0.72f));
                        g.FillEllipse(b, x + 0.44f * s, y + 0.26f * s, 0.12f * s, 0.12f * s);
                        break;
                }
        }

        // The pages every widget app shares: Look, Placement, General and About. Leave a hook null and its row isn't shown.
        public class Common
        {
            public string Title, Version, Blurb;
            public Action OpenTheme;
            public Func<int> Scale; public Action<int> SetScale;
            public int[] Widths; public Func<int> CardWidth; public Action<int> SetCardWidth;
            public Func<string> FontName; public Action<string> SetFontName;
            public string[] Fonts;
            public Func<int> Opacity; public Action<int> SetOpacity;
            public int[] Scales, Opacities;
            public Func<int> Layer; public Action<int> SetLayer;
            public Func<bool> Locked; public Action<bool> SetLocked; public string LockText = "Lock position";
            public Func<bool> ClickThrough; public Action<bool> SetClickThrough;
            public Func<bool> Snap; public Action<bool> SetSnap;
            public Func<bool> RunAtStartup; public Action<bool> SetRunAtStartup;
            public Action BackUp, Restore; public Func<string> LastBackup;

            public List<Page> Pages()
            {
                var list = new List<Page>();
                list.Add(new Page { Name = "Look", Glyph = "look", Sub = "Theme, size, font and background of the widget.", Build = b =>
                {
                    b.Section("Theme");
                    if (OpenTheme != null) b.Buttons("Widget theme", "Its colours, including Match app theme", b.Button("Choose…", OpenTheme));
                    b.Section("Size and text");
                    if (Scale != null) b.Choice("Size", null, Scales.Select(v => v + "%").ToArray(), Array.IndexOf(Scales, Scale()), i => SetScale(Scales[i]), 120);
                    if (Widths != null && CardWidth != null)
                    {
                        string[] names = { "Narrow", "Normal", "Wide" };
                        b.Choice("Width", null, names.Take(Widths.Length).ToArray(), Array.IndexOf(Widths, CardWidth()), i => SetCardWidth(Widths[i]), 120);
                    }
                    if (FontName != null)
                    {
                        var fonts = new List<string> { "Default" }; fonts.AddRange(Fonts);
                        if (FontName() != "" && !fonts.Contains(FontName())) fonts.Add(FontName());
                        b.Choice("Font", "Default is Segoe UI Variable", fonts.ToArray(), FontName() == "" ? 0 : fonts.IndexOf(FontName()), i => SetFontName(i == 0 ? "" : fonts[i]), 200);
                    }
                    if (Opacity != null) b.Choice("Background opacity", "0% leaves just the text", Opacities.Select(v => v + "%").ToArray(), Array.IndexOf(Opacities, Opacity()), i => SetOpacity(Opacities[i]), 120);
                } });
                list.Add(new Page { Name = "Placement", Glyph = "place", Sub = "Where the widget sits, and whether it can be moved.", Build = b =>
                {
                    b.Section("Placement");
                    if (Layer != null) b.Choice("Layer", "On the desktop, it stays behind your windows", new[] { "On the desktop", "Normal window", "Always on top" }, Layer(), SetLayer, 180);
                    if (Locked != null) b.Switch(LockText, "Stops it being dragged by accident", Locked(), SetLocked);
                    if (ClickThrough != null) b.Switch("Click-through when locked", "Clicks pass to whatever is underneath", ClickThrough(), SetClickThrough);
                    if (Snap != null) b.Switch("Snap to screen edges", null, Snap(), SetSnap);
                } });
                list.Add(new Page { Name = "General", Glyph = "general", Sub = "The app theme, startup and backups.", Build = b =>
                {
                    b.Section("App theme");
                    b.Choice("Theme", "Every Kinetik app's windows and menus", Look.Themes, Math.Max(0, Array.IndexOf(Look.Themes, Look.SavedTheme)), i => { Look.Save(Look.Themes[i], null); b.Refresh(); b.w.Recolour(); }, 180);
                    b.Choice("Accent colour", null, Look.Accents, Math.Max(0, Array.IndexOf(Look.Accents, Look.SavedAccent)), i => { Look.Save(null, Look.Accents[i]); b.Refresh(); b.w.Recolour(); }, 180);
                    if (RunAtStartup != null) { b.Section("Startup"); b.Switch("Run at startup", "Starts when you sign in to Windows", RunAtStartup(), SetRunAtStartup); }
                    if (BackUp != null)
                    {
                        b.Section("Backup");
                        b.Buttons("Settings backup", "Last backup: " + (LastBackup != null ? LastBackup() : "–"), b.Button("Back up", () => { BackUp(); b.Refresh(); }), b.Button("Restore…", Restore));
                    }
                } });
                list.Add(new Page { Name = "About", Glyph = "about", Sub = "Version and licence.", Build = b =>
                {
                    b.Section(Title);
                    b.Row("Version", null, new System.Windows.Forms.Label { Text = Version, AutoSize = true, ForeColor = Look.Current.Text, BackColor = Look.Current.Card, Font = b.w.UI(10f) });
                    b.Row(Blurb ?? "One of the Kinetik apps", "Free software under the GNU General Public License v3", null);
                } });
                return list;
            }
        }

        // After the app theme changes: this window in the new colours.
        internal void Recolour()
        {
            var l = Look.Current;
            BackColor = l.Bg; nav.BackColor = l.Nav; header.BackColor = l.Bg; content.BackColor = l.Bg;
            foreach (System.Windows.Forms.Control c in nav.Controls) { c.BackColor = l.Nav; foreach (System.Windows.Forms.Control n in c.Controls) { n.BackColor = l.Nav; n.Invalidate(); } }
            if (content.Parent != null) content.Parent.BackColor = l.Bg;
            l.Window(Handle);
            nav.Invalidate(); header.Invalidate();
        }
    }

    // A drop-down list whose closed box and open list are both drawn in the theme (Windows draws a light arrow box otherwise).
    public class Dropdown : System.Windows.Forms.ComboBox
    {
        bool hot;
        public Dropdown()
        {
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            var l = Look.Current; BackColor = l.Field; ForeColor = l.Text;
        }
        protected override void OnDrawItem(System.Windows.Forms.DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var l = Look.Current;
            bool edit = (e.State & System.Windows.Forms.DrawItemState.ComboBoxEdit) != 0, sel = (e.State & System.Windows.Forms.DrawItemState.Selected) != 0 && !edit;
            if (edit) return; // the closed box is drawn whole in WndProc
            using (var b = new System.Drawing.SolidBrush(sel ? l.Hover : l.Field)) e.Graphics.FillRectangle(b, e.Bounds);
            System.Windows.Forms.TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new System.Drawing.Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), sel ? l.Text : l.Text,
                System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.Left | System.Windows.Forms.TextFormatFlags.EndEllipsis | System.Windows.Forms.TextFormatFlags.NoPrefix);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PaintStruct
        {
            public IntPtr Hdc; public int Erase; public int L, T, R, B; public int Restore, IncUpdate;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PaintStruct ps);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PaintStruct ps);

        // The closed box is drawn here entirely, in place of Windows' own painting, which leaves edges of its own.
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == 0x0085) { PaintFrame(); return; } // WM_NCPAINT: the thin frame outside the box, in the row's colour
            if (m.Msg != 0x000F || DroppedDown) { base.WndProc(ref m); return; } // WM_PAINT
            PaintStruct ps;
            BeginPaint(Handle, out ps);
            try { using (var g = System.Drawing.Graphics.FromHdc(ps.Hdc)) PaintBox(g); }
            finally { EndPaint(Handle, ref ps); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        void PaintFrame()
        {
            var dc = GetWindowDC(Handle);
            if (dc == IntPtr.Zero) return;
            try
            {
                using (var g = System.Drawing.Graphics.FromHdc(dc))
                using (var pen = new System.Drawing.Pen(Parent != null ? Parent.BackColor : Look.Current.Card, 2))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            finally { ReleaseDC(Handle, dc); }
        }

        void PaintBox(System.Drawing.Graphics g)
        {
            var l = Look.Current;
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new System.Drawing.SolidBrush(Parent != null ? Parent.BackColor : l.Card)) g.FillRectangle(b, ClientRectangle);
                var r = new System.Drawing.RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
                Paths.Fill(g, r, 6, hot && Enabled ? l.Hover : l.Field);
                Paths.Stroke(g, r, 6, Focused && ShowFocusCues ? l.Accent : l.Border);
                System.Windows.Forms.TextRenderer.DrawText(g, SelectedIndex >= 0 ? GetItemText(SelectedItem) : "", Font, new System.Drawing.Rectangle(8, 0, Width - 36, Height), Enabled ? l.Text : l.Sub,
                    System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.Left | System.Windows.Forms.TextFormatFlags.EndEllipsis | System.Windows.Forms.TextFormatFlags.SingleLine | System.Windows.Forms.TextFormatFlags.NoPrefix);
                Paths.Chevron(g, new System.Drawing.RectangleF(Width - 28, 0, 20, Height), l.Sub);
            }
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
