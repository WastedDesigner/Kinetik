import SwiftUI
import KinetikFilesCore

struct ContentView: View {
    @EnvironmentObject private var window: WindowModel
    @ObservedObject private var ops = FileOperations.shared
    @ObservedObject private var settings = AppSettings.shared
    @Environment(\.look) private var look

    var body: some View {
        VStack(spacing: 0) {
            TabStrip()
            Rectangle().fill(look.border).frame(height: 1)
            HStack(spacing: 0) {
                if window.showSidebar {
                    SidebarView()
                        .frame(width: 214)
                    Rectangle().fill(look.border).frame(width: 1)
                }
                TabBody(tab: window.tab)
                    .id(window.tab.id)
            }
            Rectangle().fill(look.border).frame(height: 1)
            StatusBar(tab: window.tab)
        }
        .background(look.bg)
        .foregroundStyle(look.text)
        .ignoresSafeArea(.container, edges: .top)
        .overlay {
            if window.paletteShown {
                CommandPalette()
            }
        }
        .sheet(isPresented: $window.batchRenameShown) {
            BatchRenameView(items: window.pane.selectedItems.isEmpty ? window.pane.items : window.pane.selectedItems) { plan in
                if ops.batchRename(plan) { window.pane.reload(); window.pane.select(plan.map(\.target)) }
            }
            .themed()
        }
        .sheet(isPresented: $window.goToShown) {
            GoToView().environmentObject(window).themed()
        }
        .alert("Rename", isPresented: Binding(get: { window.renaming != nil }, set: { if !$0 { window.renaming = nil } })) {
            TextField("Name", text: $window.renameText)
            Button("Rename") { window.commitRename() }.keyboardShortcut(.defaultAction)
            Button("Cancel", role: .cancel) { window.renaming = nil }
        } message: {
            Text(window.renaming?.lastPathComponent ?? "")
        }
        .confirmationDialog(
            deleteTitle,
            isPresented: Binding(get: { window.confirmDelete != nil }, set: { if !$0 { window.confirmDelete = nil } })
        ) {
            Button("Delete", role: .destructive) { window.deleteImmediately() }
            Button("Cancel", role: .cancel) { window.confirmDelete = nil }
        } message: {
            Text("This can't be undone.")
        }
        .alert("Kinetik Files", isPresented: Binding(get: { ops.problem != nil }, set: { if !$0 { ops.problem = nil } })) {
            Button("OK", role: .cancel) { ops.problem = nil }
        } message: {
            Text(ops.problem ?? "")
        }
    }

    private var deleteTitle: String {
        let n = window.confirmDelete?.count ?? 0
        return n == 1 ? "Delete “\(window.confirmDelete?.first?.lastPathComponent ?? "")” immediately?" : "Delete \(n) items immediately?"
    }
}

/// The panes of one tab, plus the preview pane.
struct TabBody: View {
    @ObservedObject var tab: TabModel
    @EnvironmentObject private var window: WindowModel
    @Environment(\.look) private var look

    var body: some View {
        HStack(spacing: 0) {
            if tab.isDual {
                HSplitView {
                    ForEach(Array(tab.panes.enumerated()), id: \.element.id) { i, pane in
                        PaneView(pane: pane, active: tab.activeIndex == i) {
                            if tab.activeIndex != i { tab.activeIndex = i; window.touched() }
                        }
                        .frame(minWidth: 280)
                    }
                }
            } else {
                PaneView(pane: tab.active, active: true) {}
            }
            if window.showPreview {
                Rectangle().fill(look.border).frame(width: 1)
                PreviewPane(pane: tab.active)
                    .frame(width: 290)
            }
        }
    }
}

/// The tabs, drawn in the title bar beside the traffic lights.
struct TabStrip: View {
    @EnvironmentObject private var window: WindowModel
    @Environment(\.look) private var look

    var body: some View {
        HStack(spacing: 4) {
            Color.clear.frame(width: 74) // traffic lights
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 4) {
                    ForEach(window.tabs) { t in
                        TabChip(tab: t, selected: t.id == window.selectedTabID, closable: window.tabs.count > 1)
                    }
                }
            }
            IconButton(symbol: "plus", help: "New Tab (⌘T)") { window.newTab() }
            Spacer(minLength: 8)
            IconButton(symbol: "command", help: "Command Palette (⌘K)") { window.paletteShown = true }
            IconButton(symbol: "sidebar.left", help: "Sidebar (⌃⌘S)") { window.showSidebar.toggle() }
            IconButton(symbol: "rectangle.split.2x1", help: "Two Panes (⌥⌘D)") { window.toggleDual() }
            IconButton(symbol: "sidebar.right", help: "Preview (⌥⌘P)") { window.showPreview.toggle() }
                .padding(.trailing, 8)
        }
        .frame(height: 38)
        .background(look.nav)
    }
}

struct TabChip: View {
    @ObservedObject var tab: TabModel
    let selected: Bool
    let closable: Bool
    @EnvironmentObject private var window: WindowModel
    @Environment(\.look) private var look
    @State private var hover = false

    var body: some View {
        TabChipLabel(pane: tab.active, dual: tab.isDual, selected: selected, hover: hover, closable: closable) {
            window.closeTab(tab.id)
        }
        .onHover { hover = $0 }
        .onTapGesture { window.selectedTabID = tab.id; window.touched() }
        .contextMenu {
            Button("New Tab") { window.newTab() }
            Button("Close Tab") { window.closeTab(tab.id) }
            Button("Close Other Tabs") {
                for t in window.tabs where t.id != tab.id { window.closeTab(t.id) }
            }
        }
    }
}

private struct TabChipLabel: View {
    @ObservedObject var pane: PaneModel
    let dual: Bool
    let selected: Bool
    let hover: Bool
    let closable: Bool
    let close: () -> Void
    @Environment(\.look) private var look

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: dual ? "rectangle.split.2x1" : "folder")
                .font(.system(size: 11))
                .foregroundStyle(selected ? look.accent : look.sub)
            Text(pane.title).lineLimit(1).font(.system(size: 12, weight: selected ? .semibold : .regular))
            if closable && (hover || selected) {
                Button(action: close) {
                    Image(systemName: "xmark").font(.system(size: 9, weight: .bold)).foregroundStyle(look.sub)
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, 10)
        .frame(height: 26)
        .frame(minWidth: 90, maxWidth: 200)
        .background(RoundedRectangle(cornerRadius: 7).fill(selected ? look.card : hover ? look.hover : .clear))
        .overlay(RoundedRectangle(cornerRadius: 7).stroke(selected ? look.border : .clear, lineWidth: 1))
    }
}

/// Item counts, selection size, free space and what's happening.
struct StatusBar: View {
    @ObservedObject var tab: TabModel
    @ObservedObject private var ops = FileOperations.shared
    @Environment(\.look) private var look

    var body: some View {
        StatusBarContent(pane: tab.active, busy: ops.busy)
    }
}

private struct StatusBarContent: View {
    @ObservedObject var pane: PaneModel
    let busy: String?
    @Environment(\.look) private var look

    var body: some View {
        HStack(spacing: 12) {
            if let busy {
                ProgressView().controlSize(.small)
                Text(busy)
            } else if let err = pane.queryError {
                Image(systemName: "exclamationmark.triangle").foregroundStyle(look.warn)
                Text(err).foregroundStyle(look.warn)
            } else {
                Text(summary)
            }
            if pane.isSearching { ProgressView().controlSize(.small); Text("Searching…") }
            Spacer()
            Text(freeSpace)
        }
        .font(.system(size: 11))
        .foregroundStyle(look.sub)
        .padding(.horizontal, 12)
        .frame(height: 24)
        .background(look.nav)
    }

    private var summary: String {
        let n = pane.items.count
        let sel = pane.selectedItems
        var s = n == 1 ? "1 item" : "\(n) items"
        if !sel.isEmpty {
            let bytes = sel.reduce(Int64(0)) { $0 + ($1.isFolder ? (pane.folderSizes[$1.url] ?? 0) : ($1.size ?? 0)) }
            s += ", \(sel.count) selected (\(PathTools.size(bytes)))"
        }
        return s
    }

    private var freeSpace: String {
        guard let v = try? pane.location.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]),
              let free = v.volumeAvailableCapacityForImportantUsage else { return "" }
        return "\(PathTools.size(free)) available"
    }
}
