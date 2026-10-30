import SwiftUI
import AppKit
import KinetikFilesCore

/// One pane: its toolbar (back, forward, up, path, filter, view) and the files.
struct PaneView: View {
    @ObservedObject var pane: PaneModel
    let active: Bool
    let activate: () -> Void
    @EnvironmentObject private var window: WindowModel
    @ObservedObject private var settings = AppSettings.shared
    @Environment(\.look) private var look

    var body: some View {
        VStack(spacing: 0) {
            PaneToolbar(pane: pane, active: active)
            Rectangle().fill(look.border).frame(height: 1)
            ZStack {
                look.bg
                content
                if let err = pane.errorText {
                    ErrorState(text: err)
                } else if pane.items.isEmpty && !pane.isLoading && !pane.isSearching {
                    Text(pane.isFiltering ? "Nothing matches" : "This folder is empty")
                        .foregroundStyle(look.sub)
                        .allowsHitTesting(false)
                }
            }
            .dropDestination(for: URL.self) { urls, _ in
                Drops.receive(urls, into: pane.location, pane: pane)
                return true
            }
        }
        .overlay(alignment: .top) {
            // The active pane gets an accent line when there are two.
            if window.tab.isDual && active { Rectangle().fill(look.accent).frame(height: 2) }
        }
        .simultaneousGesture(TapGesture().onEnded { activate() })
        .onChange(of: pane.selection) { _, new in
            QuickLook.shared.selectionChanged(pane.selectedURLs)
            if active { window.touched() }
            _ = new
        }
    }

    @ViewBuilder private var content: some View {
        switch pane.viewMode {
        case .list: FileListView(pane: pane)
        case .grid: FileGridView(pane: pane)
        case .columns: ColumnsView(pane: pane)
        }
    }
}

private struct ErrorState: View {
    let text: String
    @Environment(\.look) private var look
    var body: some View {
        VStack(spacing: 12) {
            Image(systemName: "lock.fill").font(.system(size: 28)).foregroundStyle(look.sub)
            Text(text).multilineTextAlignment(.center).foregroundStyle(look.sub).frame(maxWidth: 380)
            if text.contains("Full Disk Access") {
                Button("Open Privacy Settings") {
                    NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles")!)
                }
            }
        }
        .padding()
    }
}

/// Back, forward, up, the path, the filter and the view switcher.
struct PaneToolbar: View {
    @ObservedObject var pane: PaneModel
    let active: Bool
    @Environment(\.look) private var look
    @FocusState private var filterFocused: Bool

    var body: some View {
        HStack(spacing: 6) {
            IconButton(symbol: "chevron.left", help: "Back (⌘[)", enabled: !pane.back.isEmpty) { pane.goBack() }
                .contextMenu { historyMenu(Array(pane.back.reversed())) }
            IconButton(symbol: "chevron.right", help: "Forward (⌘])", enabled: !pane.forward.isEmpty) { pane.goForward() }
                .contextMenu { historyMenu(Array(pane.forward.reversed())) }
            IconButton(symbol: "arrow.up", help: "Enclosing folder (⌘↑)", enabled: pane.location.path != "/") { pane.goUp() }
            AddressBar(pane: pane)
            HStack(spacing: 4) {
                KField(placeholder: "Filter or search", text: $pane.filterText, symbol: "magnifyingglass")
                    .focused($filterFocused)
                    .frame(width: 200)
                    .onExitCommand { pane.filterText = ""; filterFocused = false }
                Toggle(isOn: $pane.searchSubfolders) { Image(systemName: "square.stack.3d.down.right") }
                    .toggleStyle(.button)
                    .help("Search subfolders too")
            }
            Picker("", selection: $pane.viewMode) {
                ForEach(ViewMode.allCases) { m in Image(systemName: m.symbol).tag(m).help(m.rawValue) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(width: 104)
        }
        .padding(.horizontal, 8)
        .frame(height: 38)
        .background(look.nav.opacity(active ? 1 : 0.6))
        .onReceive(NotificationCenter.default.publisher(for: .focusFilter)) { _ in if active { filterFocused = true } }
    }

    @ViewBuilder private func historyMenu(_ urls: [URL]) -> some View {
        ForEach(Array(urls.prefix(15).enumerated()), id: \.offset) { _, u in
            Button(FileManager.default.displayName(atPath: u.path)) { pane.go(to: u) }
        }
    }
}

/// The path as clickable folders; click the empty area (or ⌘L) to type a path.
struct AddressBar: View {
    @ObservedObject var pane: PaneModel
    @State private var editing = false
    @State private var typed = ""
    @FocusState private var focused: Bool
    @Environment(\.look) private var look

    var body: some View {
        Group {
            if editing {
                TextField("Path", text: $typed)
                    .textFieldStyle(.plain)
                    .focused($focused)
                    .onSubmit {
                        if let u = PathTools.resolve(typed, relativeTo: pane.location) { pane.go(to: u) }
                        editing = false
                    }
                    .onExitCommand { editing = false }
                    .onChange(of: focused) { _, f in if !f { editing = false } }
                    .padding(.horizontal, 8)
            } else {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 0) {
                        let crumbs = PathTools.crumbs(pane.location)
                        ForEach(Array(crumbs.enumerated()), id: \.offset) { i, u in
                            Crumb(url: u, isLast: i == crumbs.count - 1, pane: pane)
                        }
                    }
                    .padding(.horizontal, 4)
                }
                .contentShape(Rectangle())
                .onTapGesture(count: 1) { startEditing() }
            }
        }
        .frame(maxWidth: .infinity, minHeight: 26, maxHeight: 26)
        .background(RoundedRectangle(cornerRadius: 6).fill(look.field))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(editing ? look.accent : look.border, lineWidth: 1))
        .background(Button("") { startEditing() }.keyboardShortcut("l", modifiers: .command).opacity(0))
        .dropDestination(for: URL.self) { urls, _ in
            if let u = urls.first { pane.go(to: u) }
            return true
        }
    }

    private func startEditing() {
        typed = pane.location.path
        editing = true
        focused = true
    }
}

private struct Crumb: View {
    let url: URL
    let isLast: Bool
    @ObservedObject var pane: PaneModel
    @Environment(\.look) private var look
    @State private var hover = false

    var body: some View {
        HStack(spacing: 2) {
            Button {
                pane.go(to: url)
            } label: {
                Text(url.path == "/" ? FileManager.default.displayName(atPath: "/") : FileManager.default.displayName(atPath: url.path))
                    .font(.system(size: 12, weight: isLast ? .semibold : .regular))
                    .foregroundStyle(isLast ? look.text : look.sub)
                    .padding(.horizontal, 5).padding(.vertical, 3)
                    .background(RoundedRectangle(cornerRadius: 5).fill(hover ? look.hover : .clear))
            }
            .buttonStyle(.plain)
            .onHover { hover = $0 }
            .dropDestination(for: URL.self) { urls, _ in
                Drops.receive(urls, into: url, pane: pane)
                return true
            }
            if !isLast {
                // The chevron lists the folders inside, to jump sideways.
                Menu {
                    ForEach(subfolders, id: \.self) { f in
                        Button(f.lastPathComponent) { pane.go(to: f) }
                    }
                } label: {
                    Image(systemName: "chevron.right").font(.system(size: 9, weight: .semibold)).foregroundStyle(look.sub)
                }
                .menuStyle(.borderlessButton)
                .menuIndicator(.hidden)
                .fixedSize()
            }
        }
    }

    private var subfolders: [URL] {
        let urls = (try? FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: [.isDirectoryKey, .isPackageKey], options: [.skipsHiddenFiles])) ?? []
        return urls.filter {
            let v = try? $0.resourceValues(forKeys: [.isDirectoryKey, .isPackageKey])
            return v?.isDirectory == true && v?.isPackage != true
        }.sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
    }
}

/// Dropping files: within one disk it moves (⌥ copies), between disks it copies (⌘ moves) — the same as Finder.
enum Drops {
    @MainActor
    static func receive(_ urls: [URL], into folder: URL, pane: PaneModel) {
        let files = urls.filter(\.isFileURL)
        guard !files.isEmpty else { return }
        let flags = NSEvent.modifierFlags
        let sameVolume = files.allSatisfy { volume($0) == volume(folder) }
        let move = flags.contains(.option) ? false : flags.contains(.command) ? true : sameVolume
        Task {
            let ops = FileOperations.shared
            let made: [URL]
            if move { made = await ops.move(files, into: folder) } else { made = await ops.copy(files, into: folder) }
            pane.reload()
            if folder.standardizedFileURL == pane.location { pane.select(made) }
        }
    }

    static func volume(_ url: URL) -> URL? {
        (try? url.resourceValues(forKeys: [.volumeURLKey]))?.volume
    }
}
