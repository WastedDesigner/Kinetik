// Runs on Windows Framework 4.8 and on the Mac .NET SDK. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using KinetikSuite;

partial class SuiteCoreTests
{
    static int passed;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    static void Reject(Action work, string name)
    { bool failed = false; try { work(); } catch { failed = true; } Check(failed, name); }
    static partial void SourceCases(string root);
    static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "Kinetik-regression-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "note.md");
            foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), new UTF32Encoding(false, true, true) })
            {
                File.WriteAllText(path, "# Note\r\n- [ ] task\r\n", encoding);
                var before = TextDocument.Read(path);
                SafeEdit.Update(path, before.Revision, doc => doc.Text.Replace("[ ]", "[x]"));
                var after = TextDocument.Read(path);
                Check(after.Text == "# Note\r\n- [x] task\r\n" && after.Encoding.CodePage == encoding.CodePage && after.Bytes.Take(encoding.GetPreamble().Length).SequenceEqual(encoding.GetPreamble()), "edit preserves encoding/BOM/CRLF " + encoding.WebName);
                Check(Directory.GetFiles(Path.Combine(root, ".kinetik-recovery")).Any(f => File.ReadAllBytes(f).SequenceEqual(before.Bytes)), "previous note bytes recoverable " + encoding.WebName);
            }
            File.WriteAllText(path, "old\n"); var old = TextDocument.Read(path); File.WriteAllText(path, "external\n");
            Reject(() => SafeEdit.Update(path, old.Revision, d => "wrong"), "stale revision rejected");
            Check(File.ReadAllText(path) == "external\n", "stale edit leaves newer bytes intact");
            old = TextDocument.Read(path);
            Reject(() => SafeEdit.Update(path, old.Revision, d => { File.WriteAllText(path, "concurrent\n"); return "wrong"; }), "edit during transformation rejected");
            Check(File.ReadAllText(path) == "concurrent\n", "concurrent content retained");
            File.WriteAllBytes(path, new byte[] { 255, 0, 128 });
            Reject(() => TextDocument.Read(path), "malformed text rejected without conversion");

            var install = Path.Combine(root, "installed"); Directory.CreateDirectory(Path.Combine(install, "lib"));
            var exe = Path.Combine(install, "App.exe"); var dll = Path.Combine(install, "lib", "A.dll");
            var update = new Dictionary<string, byte[]> { { "App.exe", Encoding.UTF8.GetBytes("new exe") }, { "lib/A.dll", Encoding.UTF8.GetBytes("new dll") } };
            for (int failure = 0; failure < 4; failure++)
            {
                File.WriteAllText(exe, "old exe"); File.WriteAllText(dll, "old dll"); int at = failure;
                Reject(() => FileTransaction.Install(install, update, Path.Combine(root, "backup-" + at), (phase, n) => { if (n == at) throw new IOException("Injected " + phase + " failure"); }), "transaction rejects failure at step " + at);
                Check(File.ReadAllText(exe) == "old exe" && File.ReadAllText(dll) == "old dll", "complete old set restored at step " + at);
                Check(!Directory.GetFiles(install, "*.stage-*", SearchOption.AllDirectories).Any(), "partial staging removed at step " + at);
            }
            var backup = Path.Combine(root, "success-backup"); FileTransaction.Install(install, update, backup);
            Check(File.ReadAllText(exe) == "new exe" && File.ReadAllText(dll) == "new dll", "complete new set installed");
            Check(File.ReadAllText(Path.Combine(backup, "App.exe")) == "old exe" && File.ReadAllText(Path.Combine(backup, "lib", "A.dll")) == "old dll", "complete previous set retained");
            Reject(() => FileTransaction.Install(install, new Dictionary<string, byte[]> { { "../escape", new byte[1] } }, Path.Combine(root, "unsafe-backup")), "transaction rejects path traversal");
            File.WriteAllText(exe, "recoverable exe"); File.WriteAllText(dll, "recoverable dll");
            var incomplete = Path.Combine(root, "incomplete-recovery");
            Reject(() => FileTransaction.Install(install, update, incomplete, (phase, n) =>
            {
                if (n == 3) { File.Delete(exe); Directory.CreateDirectory(exe); throw new IOException("Inject rollback obstruction"); }
            }), "incomplete rollback is reported");
            Check(File.ReadAllText(Path.Combine(incomplete, "App.exe")) == "recoverable exe", "old executable retained when rollback is obstructed");
            Check(File.ReadAllText(dll) == "recoverable dll", "other files recovered despite one obstructed rollback");

            var settings = new BadSettings(); SettingsValidation.Sanitize(settings);
            Check(settings.Phase == 2 && settings.FocusMin == 1 && settings.Scale == 30 && settings.Opacity == 100 && settings.Refresh == 1440 && settings.RunningUntil == "" && settings.Lat == "" && settings.Lon == "", "settings bounds, timestamp and non-finite coordinates");
            char marker = '\0'; int length = 0;
            Check(Markdown.Fence("````csharp", ref marker, ref length) && marker == '`' && length == 4, "long fence opens");
            Check(!Markdown.Fence("```", ref marker, ref length) && marker == '`', "short fence cannot close longer fence");
            Check(Markdown.Fence("````", ref marker, ref length) && marker == '\0', "matching fence closes");
            Directory.CreateDirectory(Path.Combine(root, "Templates")); File.WriteAllText(Path.Combine(root, "Templates", "example.md"), "- [ ] example");
            Directory.CreateDirectory(Path.Combine(root, ".hidden")); File.WriteAllText(Path.Combine(root, ".hidden", "hidden.md"), "- [ ] hidden");
            Check(!Markdown.Files(root, true).Any(f => f.Contains("Templates") || f.Contains(".hidden")), "template/hidden directories excluded");

            NetworkCases(); SourceCases(root);
            Console.WriteLine("RESULT: " + passed + " checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
    sealed class BadSettings
    {
        public int Phase = 99, FocusMin = 0, Scale = -50, Opacity = 999, Refresh = int.MaxValue;
        public string RunningUntil = "not a timestamp", Lat = "NaN", Lon = "Infinity";
    }
    static void NetworkCases()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new Thread(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using (var client = listener.AcceptTcpClient()) using (var stream = client.GetStream())
                    {
                        var request = new byte[4096]; stream.Read(request, 0, request.Length);
                        var head = Encoding.ASCII.GetBytes(i == 0 ? "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello" : i == 1 ? "HTTP/1.1 200 OK\r\nContent-Length: 999999\r\nConnection: close\r\n\r\n" : "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nh");
                        stream.Write(head, 0, head.Length); stream.Flush();
                        if (i == 2) Thread.Sleep(1000);
                    }
                }
                catch { }
            }
        }) { IsBackground = true }; server.Start();
        try
        {
            var url = "http://127.0.0.1:" + port + "/";
            Check(Http.Text(url, 100, "KinetikTests", CancellationToken.None, 2000) == "hello", "bounded HTTP success");
            Reject(() => Http.Get(url, 100, "KinetikTests", CancellationToken.None, 2000), "oversized response rejected");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Reject(() => Http.Get(url, 100, "KinetikTests", CancellationToken.None, 200), "stalled body cancelled");
            Check(watch.ElapsedMilliseconds < 1500, "overall HTTP deadline enforced");
        }
        finally { listener.Stop(); server.Join(2000); }
    }
}
