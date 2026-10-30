import SwiftUI
import AppKit

/// Every command, in one list: the menus, the command palette and the shortcuts all come from here.
struct AppCommand: Identifiable {
    let id: String
    let title: String
    let group: String
    let symbol: String
    let key: KeyEquivalent?
    let modifiers: EventModifiers
    let run: @MainActor (WindowModel) -> Void

    init(_ id: String, _ title: String, _ group: String, _ symbol: String, _ key: KeyEquivalent? = nil, _ modifiers: EventModifiers = .command, run: @escaping @MainActor (WindowModel) -> Void) {
        self.id = id; self.title = title; self.group = group; self.symbol = symbol
        self.key = key; self.modifiers = modifiers; self.run = run
    }

    /// "⌥⌘N" style text for the palette.
    var shortcutText: String {
        guard let key else { return "" }
        var s = ""
        if modifiers.contains(.control) { s += "⌃" }
        if modifiers.contains(.option) { s += "⌥" }
        if modifiers.contains(.shift) { s += "⇧" }
        if modifiers.contains(.command) { s += "⌘" }
        switch key {
        case .return: s += "↩"
        case .delete: s += "⌫"
        case .upArrow: s += "↑"
        case .downArrow: s += "↓"
        case .leftArrow: s += "←"
        case .rightArrow: s += "→"
        case .tab: s += "⇥"
        case .space: s += "Space"
        case .escape: s += "⎋"
        default: s += String(key.character).uppercased()
        }
        return s
    }

    static let all: [AppCommand] = [
        // File
        AppCommand("newWindow", "New Window", "File", "macwindow.badge.plus", "n") { _ in AppState.shared.open(URL(fileURLWithPath: AppSettings.shared.homeFolder)) },
        AppCommand("newTab", "New Tab", "File", "plus.square.on.square", "t") { $0.newTab() },
        AppCommand("openTab", "Open in New Tab", "File", "rectangle.badge.plus", "t", [.command, .shift]) { $0.openInNewTab() },
        AppCommand("closeTab", "Close Tab", "File", "xmark.square", "w") { $0.closeTab() },
        AppCommand("newFolder", "New Folder", "File", "folder.badge.plus", "n", [.command, .shift]) { $0.newFolder() },
        AppCommand("newFolderWith", "New Folder with Selection", "File", "folder.badge.plus", "n", [.command, .control]) { $0.newFolderWithSelection() },
        AppCommand("newText", "New Text File", "File", "doc.badge.plus", "n", [.command, .option]) { $0.newTextFile() },
        AppCommand("open", "Open", "File", "arrow.up.forward.app", .downArrow) { $0.open() },
        AppCommand("rename", "Rename", "File", "pencil") { $0.rename() }, // Return, handled by the file list
        AppCommand("batchRename", "Batch Rename…", "File", "pencil.and.list.clipboard", "r", [.command, .shift]) { $0.batchRenameShown = true },
        AppCommand("duplicate", "Duplicate", "File", "plus.square.on.square", "d") { $0.duplicate() },
        AppCommand("compress", "Compress to ZIP", "File", "doc.zipper") { $0.compress() },
        AppCommand("extract", "Extract Here", "File", "arrow.up.bin") { $0.extract() },
        AppCommand("trash", "Move to Trash", "File", "trash", .delete) { $0.trash() },
        AppCommand("delete", "Delete Immediately…", "File", "trash.slash", .delete, [.command, .option]) { $0.askDeleteImmediately() },
        AppCommand("getInfo", "Get Info", "File", "info.circle", "i") { $0.getInfo() },
        AppCommand("quickLook", "Quick Look", "File", "eye", "y") { $0.quickLook() },
        AppCommand("reveal", "Show in Finder", "File", "macwindow", "r") { $0.revealInFinder() },
        AppCommand("terminal", "Open Terminal Here", "File", "terminal", "`", .control) { $0.openTerminal() },
        AppCommand("favourite", "Add to Favourites", "File", "star", "t", [.command, .control]) { $0.addFavourite() },

        // Edit
        AppCommand("undo", "Undo", "Edit", "arrow.uturn.backward", "z") { $0.undo() },
        AppCommand("redo", "Redo", "Edit", "arrow.uturn.forward", "z", [.command, .shift]) { $0.redo() },
        AppCommand("cut", "Cut", "Edit", "scissors", "x") { $0.cut() },
        AppCommand("copy", "Copy", "Edit", "doc.on.doc", "c") { $0.copy() },
        AppCommand("paste", "Paste", "Edit", "doc.on.clipboard", "v") { $0.paste() },
        AppCommand("move", "Move Item Here", "Edit", "arrow.right.doc.on.clipboard", "v", [.command, .option]) { $0.paste(move: true) },
        AppCommand("pasteFile", "Paste as File", "Edit", "doc.badge.arrow.up", "v", [.command, .shift]) { $0.pasteAsFile() },
        AppCommand("copyPath", "Copy Path", "Edit", "link", "c", [.command, .option]) { $0.copyPath() },
        AppCommand("selectAll", "Select All", "Edit", "checkmark.circle", "a") { $0.pane.selectAll() },
        AppCommand("selectNone", "Select None", "Edit", "circle", "a", [.command, .option]) { $0.pane.selectNone() },
        AppCommand("invert", "Invert Selection", "Edit", "circle.lefthalf.filled", "i", [.command, .shift]) { $0.pane.invertSelection() },
        AppCommand("find", "Find", "Edit", "magnifyingglass", "f") { _ in NotificationCenter.default.post(name: .focusFilter, object: nil) },
        AppCommand("clearTags", "Remove All Tags", "Edit", "tag.slash") { $0.clearTags() },

        // View
        AppCommand("list", "as List", "View", "list.bullet", "1", [.command, .option]) { $0.setView(.list) },
        AppCommand("grid", "as Grid", "View", "square.grid.2x2", "2", [.command, .option]) { $0.setView(.grid) },
        AppCommand("columns", "as Columns", "View", "rectangle.split.3x1", "3", [.command, .option]) { $0.setView(.columns) },
        AppCommand("dual", "Two Panes", "View", "rectangle.split.2x1", "d", [.command, .option]) { $0.toggleDual() },
        AppCommand("switchPane", "Switch Pane", "View", "arrow.left.arrow.right", .tab, .option) { $0.switchPane() },
        AppCommand("copyOther", "Copy to Other Pane", "View", "doc.on.doc", "c", [.command, .control]) { $0.transferToOtherPane(move: false) },
        AppCommand("moveOther", "Move to Other Pane", "View", "arrow.right.doc.on.clipboard", "m", [.command, .control]) { $0.transferToOtherPane(move: true) },
        AppCommand("sidebar", "Show Sidebar", "View", "sidebar.left", "s", [.command, .control]) { $0.showSidebar.toggle() },
        AppCommand("preview", "Show Preview", "View", "sidebar.right", "p", [.command, .option]) { $0.showPreview.toggle() },
        AppCommand("hidden", "Show Hidden Files", "View", "eye.slash", ".", [.command, .shift]) { $0.toggleHidden() },
        AppCommand("zoomIn", "Bigger", "View", "plus.magnifyingglass", "=") { $0.zoom(0.1) },
        AppCommand("zoomOut", "Smaller", "View", "minus.magnifyingglass", "-") { $0.zoom(-0.1) },
        AppCommand("zoomReset", "Actual Size", "View", "1.magnifyingglass", "0") { $0.zoom(0) },
        AppCommand("reload", "Refresh", "View", "arrow.clockwise", "r", [.command, .option]) { $0.reload() },
        AppCommand("palette", "Command Palette", "View", "command", "k") { $0.paletteShown = true },

        // Go
        AppCommand("back", "Back", "Go", "chevron.left", "[") { $0.back() },
        AppCommand("forward", "Forward", "Go", "chevron.right", "]") { $0.forward() },
        AppCommand("up", "Enclosing Folder", "Go", "arrow.up", .upArrow) { $0.up() },
        AppCommand("goTo", "Go to Folder…", "Go", "arrow.right.circle", "g", [.command, .shift]) { $0.goToShown = true },
        AppCommand("home", "Home", "Go", "house", "h", [.command, .shift]) { $0.home() },
        AppCommand("desktop", "Desktop", "Go", "menubar.dock.rectangle", "d", [.command, .shift]) { $0.go(to: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Desktop")) },
        AppCommand("downloads", "Downloads", "Go", "arrow.down.circle", "l", [.command, .option]) { $0.go(to: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads")) },
        AppCommand("documents", "Documents", "Go", "doc", "o", [.command, .shift]) { $0.go(to: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Documents")) },
        AppCommand("applications", "Applications", "Go", "app.dashed", "a", [.command, .shift]) { $0.go(to: URL(fileURLWithPath: "/Applications")) },
        AppCommand("icloud", "iCloud Drive", "Go", "icloud", "i", [.command, .shift, .option]) { $0.go(to: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Mobile Documents/com~apple~CloudDocs")) },
        AppCommand("nextTab", "Next Tab", "Go", "chevron.right.square", .tab, .control) { $0.selectTab(offset: 1) },
        AppCommand("prevTab", "Previous Tab", "Go", "chevron.left.square", .tab, [.control, .shift]) { $0.selectTab(offset: -1) },
    ]

    static func find(_ id: String) -> AppCommand? { all.first { $0.id == id } }

    /// Edit commands that a text field should get instead while someone is typing in it.
    static let textSelectors: [String: Selector] = [
        "cut": #selector(NSText.cut(_:)),
        "copy": #selector(NSText.copy(_:)),
        "paste": #selector(NSText.paste(_:)),
        "selectAll": #selector(NSText.selectAll(_:)),
        "undo": Selector(("undo:")),
        "redo": Selector(("redo:")),
    ]

    /// Runs the command, or hands Edit commands to the text field being typed in.
    @MainActor
    func perform(in window: WindowModel?) {
        if let sel = AppCommand.textSelectors[id], NSApp.keyWindow?.firstResponder is NSText {
            NSApp.sendAction(sel, to: nil, from: nil)
            return
        }
        guard let window else { return }
        run(window)
    }
}

extension Notification.Name {
    static let focusFilter = Notification.Name("KinetikFiles.focusFilter")
}
