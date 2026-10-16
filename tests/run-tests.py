#!/usr/bin/env python3
"""Run real shared code and extracted app algorithms, without launching a GUI."""
from pathlib import Path
import argparse
import json
import os
import re
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--workspace', type=Path)
parser.add_argument('--log', type=Path)
args = parser.parse_args()
tests = Path(__file__).resolve().parent
app = tests.parent
root = args.workspace.resolve() if args.workspace else app
sources = [p for p in root.rglob('Kinetik*.cs') if 'Old Versions' not in p.parts and 'Diagnostics' not in p.parts and '.git' not in p.parts]
modules = ['using System; using System.Collections.Generic; using System.Linq; using System.IO; using System.Text; using System.Text.RegularExpressions; using System.Globalization; using System.Drawing; using Microsoft.Win32; using System.Windows.Forms;']
cases = []
for source in sources:
    text = source.read_text()
    if source.stem == 'KinetikCalendar':
        modules.append('class Feed {}\n' + text[text.index('class CalEvent\n'):text.index('// ======================================================================= Demo')])
        cases.append('''var recurrence = typeof(Ics).GetMethod("Recur", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var occurrences = (IEnumerable<DateTime>)recurrence.Invoke(null, new object[] { new DateTime(2024,1,31,9,0,0), "FREQ=DAILY;BYMONTHDAY=-1;COUNT=3", TimeZoneInfo.Utc, new DateTime(2024,4,1) });
Check(occurrences.Select(d => d.ToString("yyyy-MM-dd")).SequenceEqual(new[] { "2024-01-31", "2024-02-29", "2024-03-31" }), "actual Calendar negative month-day and COUNT across leap February");
var until = (IEnumerable<DateTime>)recurrence.Invoke(null, new object[] { new DateTime(2025,1,31,9,0,0), "FREQ=DAILY;BYMONTHDAY=-1;UNTIL=20250228T090000Z", TimeZoneInfo.Utc, new DateTime(2025,4,1) });
Check(until.Count() == 2, "actual Calendar last-day UNTIL is inclusive");
var positive = (IEnumerable<DateTime>)recurrence.Invoke(null, new object[] { new DateTime(2025,1,1,9,0,0), "FREQ=DAILY;BYMONTHDAY=1;COUNT=2", TimeZoneInfo.Utc, new DateTime(2025,4,1) });
Check(positive.Count() == 2, "actual Calendar positive month-day retained");''')
    elif source.stem == 'KinetikNotes':
        regex = re.search(r'^    static readonly Regex TaskLineRx.*$', text, re.M).group(0)
        methods = text[text.index('    public static bool Toggle('):text.index('    // Opens a note')]
        modules.append('class NotesProbe {\n' + regex + '\n' + methods + '\n}')
        cases.append('''var note = Path.Combine(root, "notes-task.md"); File.WriteAllText(note, "- [ ] A\\n- [ ] B\\n");
var revision = KinetikSuite.TextDocument.Read(note).Revision;
File.WriteAllText(note, "- [ ] inserted\\n- [ ] A\\n- [ ] B\\n");
Check(!NotesProbe.Toggle(note, 1, revision) && !File.ReadAllText(note).Contains("[x]"), "actual Notes stale line cannot tick a different task");
revision = KinetikSuite.TextDocument.Read(note).Revision;
Check(NotesProbe.Toggle(note, 2, revision) && File.ReadAllText(note).Contains("[x] B"), "actual Notes current revision ticks intended task");
revision = KinetikSuite.TextDocument.Read(note).Revision;
Check(NotesProbe.SetMeta(note, revision, "pinned", "true") && File.ReadAllText(note).StartsWith("---\\npinned: true\\n---\\n"), "actual Notes metadata uses safe edit");''')
    elif source.stem == 'KinetikFocus':
        modules.append('class Settings { public string TasksFolder; public int TaskScanLimit = 1000; }\n' + text[text.index('class TaskItem {'):text.index('// ======================================================================= The log')])
        cases.append('''var folder = Path.Combine(root, "focus"); Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.Combine(folder, "Templates"));
File.WriteAllText(Path.Combine(folder, "Templates", "template.md"), "- [ ] template task\\n");
var taskFile = Path.Combine(folder, "work.md"); File.WriteAllText(taskFile, "# Work\\n~~~markdown\\n- [ ] example\\n~~~\\n- [ ] real\\n- [ ] real\\n");
var groups = Tasks.Load(new Settings { TasksFolder = folder }); var items = groups.SelectMany(g => g.Value).ToList();
Check(items.Count == 2 && items.All(t => t.Text == "real"), "actual Focus excludes templates and fenced tasks");
var selected = items[1];
Check(Tasks.Tick(selected.File, selected.Line, selected.Text, selected.Revision) && File.ReadAllText(taskFile).EndsWith("- [ ] real\\n- [x] real\\n"), "actual Focus disambiguates duplicate tasks by revision and line");
Reject(() => Tasks.Tick(selected.File, selected.Line, selected.Text, selected.Revision), "actual Focus rejects obsolete task revision");''')
modules.append('namespace System.Windows.Forms { enum MessageBoxButtons { OK } enum MessageBoxIcon { Information, Warning } static class MessageBox { public static void Show(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon) {} } }')
modules.append('partial class SuiteCoreTests { static partial void SourceCases(string root) {\n' + '\n'.join(cases) + '\n} }')
with tempfile.TemporaryDirectory(prefix='kinetik-tests-') as directory:
    temp = Path(directory)
    generated = temp / 'AppAlgorithms.cs'; generated.write_text('\n'.join(modules))
    core = tests / 'SuiteCoreTests.cs'
    support = app / 'SuiteSupport.cs'
    if os.name == 'nt':
        csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        binary = temp / 'SuiteTests.exe'
        command = [str(csc), '/nologo', '/define:SUITE_CORE_TEST', '/nowarn:0649', '/target:exe', '/out:' + str(binary), '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', str(support), str(core), str(generated)]
        run = [str(binary)]
    else:
        dotnet = shutil.which('dotnet')
        version, sdkdir = re.findall(r'^([^ ]+) \[([^\]]+)\]', subprocess.check_output([dotnet, '--list-sdks'], text=True), re.M)[-1]
        runtime_root = Path(sdkdir).parent
        ref = sorted((runtime_root / 'packs/Microsoft.NETCore.App.Ref').glob('*/ref/net*'))[-1]
        framework = ref.name; runtime_version = ref.parent.parent.name
        binary = temp / 'SuiteTests.dll'
        command = [dotnet, str(Path(sdkdir) / version / 'Roslyn/bincore/csc.dll'), '-nologo', '-noconfig', '-nostdlib', '-define:SUITE_CORE_TEST', '-nowarn:0649', '-target:exe', '-out:' + str(binary)]
        command += ['-r:' + str(p) for p in ref.glob('*.dll')] + [str(support), str(core), str(generated)]
        (temp / 'SuiteTests.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': framework, 'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime_version}}}))
        run = [dotnet, str(binary)]
    build = subprocess.run(command, capture_output=True, text=True)
    output = build.stdout + build.stderr
    if build.returncode == 0:
        result = subprocess.run(run, capture_output=True, text=True, timeout=60)
        output += result.stdout + result.stderr
        code = result.returncode
    else:
        code = build.returncode
    print(output, end='')
    if args.log:
        args.log.write_text(output)
    raise SystemExit(code)
