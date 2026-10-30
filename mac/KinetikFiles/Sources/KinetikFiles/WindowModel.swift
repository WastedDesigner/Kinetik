import SwiftUI
import AppKit
import KinetikFilesCore

/// One tab: one pane, or two side by side.
@MainActor
final class TabModel: ObservableObject, Identifiable {
    let id = UUID()
    @Published private(set) var panes: [PaneModel]
    @Published var activeIndex = 0

    init(_ url: URL) { panes = [PaneModel(location: url)] }

    var active: PaneModel { panes[min(activeIndex, panes.count - 1)] }
    var other: PaneModel? { panes.count > 1 ? panes[activeIndex == 0 ? 1 : 0] : nil }
    var isDual: Bool { panes.count > 1 }

    func toggleDual() {
        if isDual {
            let keep = active
            panes.filter { $0 !== keep }.forEach { $0.close() }
            panes = [keep]; activeIndex = 0
        } else {
            panes.append(PaneModel(location: active.location))
            activeIndex = 1
        }
    }

    func switchPane() { if isDual { activeIndex = activeIndex == 0 ? 1 : 0 } }
    func close() { panes.forEach { $0.close() } }
}

/// A Kinetik Files window: its tabs, which panels are showing, and every command.
@MainActor
final class WindowModel: ObservableObject {
    @Published var tabs: [TabModel]
    @Published var selectedTabID: UUID
    @Published var showSidebar = true
    @Published var showPreview = true
    @Published var paletteShown = false
    @Published var batchRenameShown = false
    @Published var renaming: URL?
    @Published var renameText = ""
    @Published var confirmDelete: [URL]?
    @Published var goToShown = false
    /// Bumped whenever the active pane changes, so views that only watch the window refresh.
    @Published private(set) var activeTick = 0

    let ops = FileOperations.shared
    let settings = AppSettings.shared

    init(url: URL) {
        let t = TabModel(url)
        tabs = [t]
        selectedTabID = t.id
    }

    var tab: TabModel { tabs.first { $0.id == selectedTabID } ?? tabs[0] }
    var pane: PaneModel { tab.active }

    func touched() { activeTick &+= 1 }

    // MARK: - Tabs

    func newTab(_ url: URL? = nil) {
        let t = TabModel(url ?? pane.location)
        if let i = tabs.firstIndex(where: { $0.id == selectedTabID }) { tabs.insert(t, at: i + 1) } else { tabs.append(t) }
        selectedTabID = t.id
        touched()
    }

    func closeTab(_ id: UUID? = nil) {
        let target = id ?? selectedTabID
        guard let i = tabs.firstIndex(where: { $0.id == target }) else { return }
        if tabs.count == 1 { NSApp.keyWindow?.performClose(nil); return }
        tabs[i].close()
        tabs.remove(at: i)
        if target == selectedTabID { selectedTabID = tabs[min(i, tabs.count - 1)].id }
        touched()
    }

    func selectTab(offset: Int) {
        guard let i = tabs.firstIndex(where: { $0.id == selectedTabID }) else { return }
        selectedTabID = tabs[(i + offset + tabs.count) % tabs.count].id
        touched()
    }

    func selectTab(number: Int) {
        // ⌘9 is always the last tab, like browsers.
        let i = number == 9 ? tabs.count - 1 : number - 1
        if tabs.indices.contains(i) { selectedTabID = tabs[i].id; touched() }
    }

    func moveTab(from: IndexSet, to: Int) { tabs.move(fromOffsets: from, toOffset: to) }

    /// Makes `p` the active pane and selects `urls` in it (used before running a right-click command).
    func focus(_ p: PaneModel, select urls: [URL]? = nil) {
        if let i = tab.panes.firstIndex(where: { $0 === p }), tab.activeIndex != i { tab.activeIndex = i; touched() }
        if let urls, !urls.isEmpty, Set(urls) != p.selection { p.select(urls) }
    }

    func toggleDual() { tab.toggleDual(); touched() }
    func switchPane() { tab.switchPane(); touched() }

    // MARK: - Navigation

    func go(to url: URL) { pane.go(to: url); touched() }
    func back() { pane.goBack(); touched() }
    func forward() { pane.goForward(); touched() }
    func up() { pane.goUp(); touched() }
    func home() { go(to: URL(fileURLWithPath: settings.homeFolder)) }
    func open() { pane.openSelection(); touched() }
    func openInNewTab() {
        let folders = pane.selectedItems.filter(\.isFolder)
        if folders.isEmpty { newTab() } else { folders.forEach { newTab($0.url) } }
    }
    func reload() { pane.reload() }
    func setView(_ m: ViewMode) { pane.viewMode = m; touched() }

    // MARK: - Clipboard

    func copy() { if !pane.selectedURLs.isEmpty { ops.copyToPasteboard(pane.selectedURLs, cut: false) } }
    func cut() { if !pane.selectedURLs.isEmpty { ops.copyToPasteboard(pane.selectedURLs, cut: true); touched() } }
    func paste(move: Bool = false) {
        let p = pane
        Task { let made = await ops.paste(into: p.location, forceMove: move); p.reload(); if !made.isEmpty { p.select(made) } }
    }
    func pasteAsFile() {
        let p = pane
        Task { let made = await ops.pasteContentAsFile(into: p.location); p.reload(); p.select(made) }
    }
    func copyPath() {
        let urls = pane.selectedURLs.isEmpty ? [pane.location] : pane.selectedURLs
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(urls.map(\.path).joined(separator: "\n"), forType: .string)
    }

    /// Copy or move the selection to the other pane (F5 / F6 in classic two-pane managers).
    func transferToOtherPane(move: Bool) {
        guard let other = tab.other else { return }
        let urls = pane.selectedURLs, p = pane
        guard !urls.isEmpty else { return }
        Task {
            let made: [URL]
            if move { made = await ops.move(urls, into: other.location) } else { made = await ops.copy(urls, into: other.location) }
            p.reload(); other.reload(); other.select(made)
        }
    }

    // MARK: - File changes

    func duplicate() {
        let p = pane, urls = pane.selectedURLs
        guard !urls.isEmpty else { return }
        Task { let made = await ops.duplicate(urls); p.reload(); p.select(made) }
    }

    func trash() {
        let p = pane, urls = pane.selectedURLs
        guard !urls.isEmpty else { return }
        // Select the item after the last one trashed, so the keyboard keeps working.
        let lastIndex = p.items.lastIndex { urls.contains($0.url) } ?? -1
        let next = p.items.indices.contains(lastIndex + 1) ? p.items[lastIndex + 1] : p.items.last { !urls.contains($0.url) }
        Task { await ops.trash(urls); p.reload(); if let next { p.select([next.url]) } }
    }

    func askDeleteImmediately() { if !pane.selectedURLs.isEmpty { confirmDelete = pane.selectedURLs } }

    func deleteImmediately() {
        guard let urls = confirmDelete else { return }
        confirmDelete = nil
        let p = pane
        Task { await ops.deleteImmediately(urls); p.reload() }
    }

    func newFolder() {
        guard let u = ops.newFolder(in: pane.location) else { return }
        let p = pane
        p.reload()
        Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(150))
            p.select([u]); self?.beginRename(u)
        }
    }

    func newFolderWithSelection() {
        let p = pane, urls = pane.selectedURLs
        guard !urls.isEmpty else { return }
        Task { if let f = await ops.newFolder(with: urls) { p.reload(); p.select([f]); beginRename(f) } }
    }

    func newTextFile() {
        guard let u = ops.newTextFile(in: pane.location) else { return }
        let p = pane
        p.reload()
        Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(150))
            p.select([u]); self?.beginRename(u)
        }
    }

    func rename() {
        let sel = pane.selectedURLs
        if sel.count > 1 { batchRenameShown = true } else if let u = sel.first { beginRename(u) }
    }

    func beginRename(_ url: URL) {
        renameText = url.lastPathComponent
        renaming = url
    }

    func commitRename() {
        guard let u = renaming else { return }
        renaming = nil
        if let new = ops.rename(u, to: renameText) { pane.reload(); pane.select([new]) }
    }

    func compress() {
        let p = pane, urls = pane.selectedURLs
        guard !urls.isEmpty else { return }
        Task { if let z = await ops.compress(urls) { p.reload(); p.select([z]) } }
    }

    func extract() {
        let p = pane
        let zips = pane.selectedItems.filter { $0.ext == "zip" }
        guard !zips.isEmpty else { return }
        Task {
            var made: [URL] = []
            for z in zips { if let f = await ops.extract(z.url) { made.append(f) } }
            p.reload(); p.select(made)
        }
    }

    func toggleTag(_ tag: String) {
        let items = pane.selectedItems
        guard !items.isEmpty else { return }
        let allHave = items.allSatisfy { $0.tags.contains(tag) }
        ops.setTags(items.map(\.url), add: allHave ? nil : tag, remove: allHave ? tag : nil)
        pane.reload()
    }

    func clearTags() {
        ops.setTags(pane.selectedURLs, replaceWith: [])
        pane.reload()
    }

    func undo() { let p = pane, o = tab.other; Task { await ops.undo(); p.reload(); o?.reload() } }
    func redo() { let p = pane, o = tab.other; Task { await ops.redo(); p.reload(); o?.reload() } }

    // MARK: - Mac integration

    func revealInFinder() {
        let urls = pane.selectedURLs
        if urls.isEmpty { NSWorkspace.shared.selectFile(nil, inFileViewerRootedAtPath: pane.location.path) }
        else { NSWorkspace.shared.activateFileViewerSelecting(urls) }
    }

    func openTerminal() {
        let folder = pane.selectedItems.count == 1 && pane.selectedItems[0].isFolder ? pane.selectedItems[0].url : pane.location
        Integration.openTerminal(at: folder, app: settings.terminal)
    }

    func getInfo() { Integration.showFinderInfo(pane.selectedURLs.isEmpty ? [pane.location] : pane.selectedURLs) }

    func quickLook() { QuickLook.shared.toggle(urls: pane.selectedURLs) }

    func addFavourite() {
        let urls = pane.selectedItems.filter(\.isFolder).map(\.url.path)
        let add = urls.isEmpty ? [pane.location.path] : urls
        for p in add where !settings.favourites.contains(p) { settings.favourites.append(p) }
    }

    func toggleHidden() { settings.showHidden.toggle(); tabs.forEach { $0.panes.forEach { $0.reload() } } }

    func zoom(_ delta: Double) {
        settings.zoom = delta == 0 ? 1 : min(1.8, max(0.75, settings.zoom + delta))
    }
}
