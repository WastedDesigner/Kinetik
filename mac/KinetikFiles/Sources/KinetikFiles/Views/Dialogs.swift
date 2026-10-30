import SwiftUI
import AppKit
import KinetikFilesCore

// MARK: - Command palette (⌘K)

struct CommandPalette: View {
    @EnvironmentObject private var window: WindowModel
    @ObservedObject private var settings = AppSettings.shared
    @Environment(\.look) private var look
    @State private var text = ""
    @State private var index = 0
    @FocusState private var focused: Bool

    private struct Entry: Identifiable {
        let id: String
        let title: String
        let detail: String
        let symbol: String
        let run: @MainActor () -> Void
    }

    private var entries: [Entry] {
        var all = AppCommand.all.map { c in
            Entry(id: c.id, title: c.title, detail: c.shortcutText, symbol: c.symbol) { c.perform(in: window) }
        }
        all += settings.favourites.map { p in
            Entry(id: "fav:" + p, title: "Go to " + FileManager.default.displayName(atPath: p), detail: p, symbol: "folder") {
                window.go(to: URL(fileURLWithPath: p))
            }
        }
        let q = text.trimmingCharacters(in: .whitespaces)
        if q.hasPrefix("/") || q.hasPrefix("~") {
            all.insert(Entry(id: "path", title: "Go to " + q, detail: "", symbol: "arrow.right.circle") {
                if let u = PathTools.resolve(q, relativeTo: window.pane.location) { window.go(to: u) }
            }, at: 0)
        }
        guard !q.isEmpty, !(q.hasPrefix("/") || q.hasPrefix("~")) else { return all }
        return all
            .compactMap { e -> (Entry, Int)? in Fuzzy.score(q, e.title).map { (e, $0) } }
            .sorted { $0.1 > $1.1 }
            .map(\.0)
    }

    var body: some View {
        let list = entries
        ZStack(alignment: .top) {
            Color.black.opacity(0.25).ignoresSafeArea().onTapGesture { close() }
            VStack(spacing: 0) {
                HStack(spacing: 8) {
                    Image(systemName: "command").foregroundStyle(look.sub)
                    TextField("Type a command or a path", text: $text)
                        .textFieldStyle(.plain)
                        .font(.system(size: 15))
                        .focused($focused)
                        .onSubmit { runSelected(list) }
                        .onKeyPress(.downArrow) { index = min(index + 1, max(0, list.count - 1)); return .handled }
                        .onKeyPress(.upArrow) { index = max(index - 1, 0); return .handled }
                        .onExitCommand { close() }
                }
                .padding(12)
                Rectangle().fill(look.border).frame(height: 1)
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 2) {
                            ForEach(Array(list.prefix(60).enumerated()), id: \.element.id) { i, e in
                                HStack(spacing: 10) {
                                    Image(systemName: e.symbol).frame(width: 18).foregroundStyle(i == index ? look.accent : look.sub)
                                    Text(e.title).lineLimit(1)
                                    Spacer()
                                    Text(e.detail).foregroundStyle(look.sub).font(.system(size: 11)).lineLimit(1)
                                }
                                .padding(.horizontal, 10).frame(height: 30)
                                .background(RoundedRectangle(cornerRadius: 6).fill(i == index ? look.accent.opacity(0.18) : .clear))
                                .contentShape(Rectangle())
                                .onTapGesture { index = i; runSelected(list) }
                                .id(i)
                            }
                        }
                        .padding(6)
                    }
                    .onChange(of: index) { _, i in proxy.scrollTo(i) }
                }
                .frame(maxHeight: 360)
            }
            .frame(width: 560)
            .background(RoundedRectangle(cornerRadius: 12).fill(look.card))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(look.border, lineWidth: 1))
            .shadow(color: .black.opacity(0.35), radius: 24, y: 10)
            .padding(.top, 70)
        }
        .onAppear { focused = true }
        .onChange(of: text) { _, _ in index = 0 }
    }

    private func runSelected(_ list: [Entry]) {
        guard list.indices.contains(index) else { return }
        let e = list[index]
        close()
        DispatchQueue.main.async { e.run() }
    }

    private func close() { window.paletteShown = false }
}

enum Fuzzy {
    /// Letters of `query` in order in `text`; higher for word starts and runs. Nil when it doesn't match.
    static func score(_ query: String, _ text: String) -> Int? {
        let q = Array(query.lowercased()), t = Array(text.lowercased())
        var qi = 0, score = 0, run = 0
        for (ti, c) in t.enumerated() where qi < q.count {
            if c == q[qi] {
                let wordStart = ti == 0 || t[ti - 1] == " "
                run += 1
                score += 1 + (wordStart ? 8 : 0) + run * 2
                qi += 1
            } else {
                run = 0
            }
        }
        if qi < q.count { return nil }
        if text.lowercased().hasPrefix(query.lowercased()) { score += 50 }
        return score - t.count / 8
    }
}

// MARK: - Go to folder (⌘⇧G)

struct GoToView: View {
    @EnvironmentObject private var window: WindowModel
    @Environment(\.dismiss) private var dismiss
    @Environment(\.look) private var look
    @State private var text = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Go to Folder").font(.headline)
            KField(placeholder: "/path, ~/folder, or a folder in this one", text: $text, symbol: "folder")
                .onSubmit(go)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Go", action: go).keyboardShortcut(.defaultAction)
            }
        }
        .padding(18)
        .frame(width: 440)
        .background(look.card)
        .onAppear { text = window.pane.location.path }
    }

    private func go() {
        if let u = PathTools.resolve(text, relativeTo: window.pane.location) { window.go(to: u) }
        dismiss()
    }
}

// MARK: - Batch rename (⌘⇧R)

struct BatchRenameView: View {
    let items: [FileItem]
    let apply: ([RenamePlanEntry]) -> Void
    @Environment(\.dismiss) private var dismiss
    @Environment(\.look) private var look
    @State private var o = RenameOptions()

    private var plan: [RenamePlanEntry] {
        let fm = FileManager.default
        return BatchRename.plan(items.map { RenameItem(url: $0.url, modified: $0.modified, created: $0.created) }, o) { fm.fileExists(atPath: $0.path) }
    }

    var body: some View {
        let p = plan
        let problems = p.filter { $0.problem != nil }.count
        let changes = p.filter(\.changed).count
        VStack(alignment: .leading, spacing: 14) {
            Text("Rename \(items.count) items").font(.headline)

            Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 8) {
                GridRow {
                    Text("New name").foregroundStyle(look.sub)
                    KField(placeholder: "[Name]   e.g. Holiday [N:3]   or   [Date:yyyy-MM-dd] [Name]", text: $o.pattern)
                }
                GridRow {
                    Text("Find").foregroundStyle(look.sub)
                    HStack {
                        KField(placeholder: "Text to find", text: $o.find)
                        Toggle("Regex", isOn: $o.useRegex)
                        Toggle("Match case", isOn: $o.matchCase)
                    }
                }
                GridRow {
                    Text("Replace with").foregroundStyle(look.sub)
                    KField(placeholder: o.useRegex ? "Use $1, $2 for groups" : "Replacement", text: $o.replace)
                }
                GridRow {
                    Text("Case").foregroundStyle(look.sub)
                    HStack {
                        Picker("", selection: $o.caseChange) {
                            ForEach(CaseChange.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                        }
                        .labelsHidden().frame(width: 150)
                        Text("Start at").foregroundStyle(look.sub)
                        TextField("", value: $o.startNumber, format: .number).frame(width: 50)
                        Text("Step").foregroundStyle(look.sub)
                        TextField("", value: $o.step, format: .number).frame(width: 40)
                        Toggle("Keep extensions", isOn: $o.keepExtension)
                    }
                }
            }

            Text("Tokens: [Name] [Ext] [N] [N:3] [Parent] [Date] [Date:yyyy-MM-dd] [Created:…]")
                .font(.system(size: 11)).foregroundStyle(look.sub)

            Table(Array(p.enumerated()), id: \.offset) {
                TableColumn("Now") { row in Text(row.element.source.lastPathComponent).lineLimit(1) }
                TableColumn("After") { row in
                    HStack {
                        Text(row.element.newName).lineLimit(1)
                            .foregroundStyle(row.element.problem != nil ? look.warn : row.element.changed ? look.accent : look.text)
                        if let problem = row.element.problem {
                            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(look.warn).help(problem)
                        }
                    }
                }
            }
            .frame(minHeight: 240)

            HStack {
                if problems > 0 {
                    Text("\(problems) name\(problems == 1 ? " has a problem" : "s have problems")").foregroundStyle(look.warn)
                } else {
                    Text("\(changes) will change").foregroundStyle(look.sub)
                }
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Rename") { apply(p); dismiss() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(problems > 0 || changes == 0)
            }
        }
        .padding(18)
        .frame(width: 720, height: 560)
        .background(look.card)
    }
}

// MARK: - Settings

struct SettingsView: View {
    @ObservedObject private var settings = AppSettings.shared
    @ObservedObject private var theme = ThemeStore.shared
    @State private var defaultViewer = Integration.isDefaultViewer

    var body: some View {
        TabView {
            Form {
                Section("Start") {
                    TextField("New windows open", text: $settings.homeFolder)
                    Toggle("Keep running after the last window closes", isOn: $settings.keepRunning)
                }
                Section("Use Kinetik Files instead of Finder") {
                    Toggle("Open folders and “Show in Finder” with Kinetik Files", isOn: $defaultViewer)
                        .onChange(of: defaultViewer) { _, on in Integration.setDefaultViewer(on) }
                    Text("Apps that reveal files through macOS (most of them) will open Kinetik Files instead. Finder still runs the desktop and the Trash. Turn this off to give everything back to Finder.")
                        .font(.caption).foregroundStyle(.secondary)
                    Toggle("⌥⌘E opens Kinetik Files from anywhere", isOn: $settings.hotkeyEnabled)
                }
                Section("Files") {
                    Picker("Terminal", selection: $settings.terminal) {
                        ForEach(TerminalApp.allCases) { Text($0.rawValue).tag($0) }
                    }
                    Toggle("Work out folder sizes", isOn: $settings.calculateFolderSizes)
                    Toggle("Ask before moving to the Trash", isOn: $settings.confirmTrash)
                }
            }
            .formStyle(.grouped)
            .tabItem { Label("General", systemImage: "gearshape") }

            Form {
                Section("Kinetik apps") {
                    Picker("App theme", selection: $theme.theme) {
                        ForEach(Look.themes, id: \.self) { Text($0).tag($0) }
                    }
                    Picker("Accent colour", selection: $theme.accent) {
                        ForEach(Look.accents, id: \.self) { Text($0).tag($0) }
                    }
                    Text("Picking a theme here changes every Kinetik app on this Mac.").font(.caption).foregroundStyle(.secondary)
                }
                Section("Lists") {
                    Picker("Rows", selection: $settings.density) {
                        ForEach(RowDensity.allCases) { Text($0.rawValue).tag($0) }
                    }
                    Toggle("Show hidden files", isOn: $settings.showHidden)
                    Toggle("Show file extensions", isOn: $settings.showExtensions)
                    Toggle("Folders first", isOn: $settings.foldersFirst)
                }
            }
            .formStyle(.grouped)
            .tabItem { Label("Look", systemImage: "paintpalette") }

            VStack(spacing: 8) {
                Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 72, height: 72)
                Text("Kinetik Files").font(.title2.bold())
                Text("Version \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev")").foregroundStyle(.secondary)
                Text("By Akila Sella Hennedige · GPL-3.0").foregroundStyle(.secondary)
                Link("github.com/WastedDesigner/Kinetik", destination: URL(string: "https://github.com/WastedDesigner/Kinetik")!)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .tabItem { Label("About", systemImage: "info.circle") }
        }
        .frame(width: 520, height: 440)
    }
}
