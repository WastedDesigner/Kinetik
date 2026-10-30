import SwiftUI
import AppKit
import KinetikFilesCore

// The three ways to show a folder: a list with columns, a thumbnail grid, and Miller columns.

// MARK: - Right-click menu

struct FileContextMenu: View {
    let urls: [URL]
    @ObservedObject var pane: PaneModel
    @EnvironmentObject private var window: WindowModel

    private var items: [FileItem] { pane.items.filter { urls.contains($0.url) } }

    var body: some View {
        if urls.isEmpty {
            // Right-click on empty space: the folder itself.
            Button("New Folder") { run { $0.newFolder() } }
            Button("New Text File") { run { $0.newTextFile() } }
            Button("Paste") { run { $0.paste() } }.disabled(!FileOperations.shared.pasteboardHasFiles)
            Divider()
            Button("Open Terminal Here") { run { $0.openTerminal() } }
            Button("Show in Finder") { run { $0.revealInFinder() } }
            Button("Copy Path") { run { $0.copyPath() } }
            Button("Get Info") { run { $0.getInfo() } }
        } else {
            Button("Open") { run { $0.open() } }
            if items.contains(where: \.isFolder) { Button("Open in New Tab") { run { $0.openInNewTab() } } }
            OpenWithMenu(urls: urls)
            Button("Quick Look") { run { $0.quickLook() } }
            Divider()
            Button("Rename…") { run { $0.rename() } }
            if urls.count > 1 { Button("Batch Rename…") { run { $0.batchRenameShown = true } } }
            Button("Duplicate") { run { $0.duplicate() } }
            Button("Compress to ZIP") { run { $0.compress() } }
            if items.contains(where: { $0.ext == "zip" }) { Button("Extract Here") { run { $0.extract() } } }
            if urls.count > 1 { Button("New Folder with \(urls.count) Items") { run { $0.newFolderWithSelection() } } }
            Divider()
            Button("Cut") { run { $0.cut() } }
            Button("Copy") { run { $0.copy() } }
            Button("Copy Path") { run { $0.copyPath() } }
            if window.tab.isDual {
                Button("Copy to Other Pane") { run { $0.transferToOtherPane(move: false) } }
                Button("Move to Other Pane") { run { $0.transferToOtherPane(move: true) } }
            }
            Divider()
            Menu("Tags") {
                ForEach(TagColours.names, id: \.self) { t in
                    Button {
                        run { $0.toggleTag(t) }
                    } label: {
                        Label(t, systemImage: items.allSatisfy { $0.tags.contains(t) } ? "checkmark.circle.fill" : "circle.fill")
                    }
                }
                Divider()
                Button("Remove All Tags") { run { $0.clearTags() } }
            }
            ShareLink(items: urls) { Text("Share…") }
            Divider()
            Button("Show in Finder") { run { $0.revealInFinder() } }
            Button("Open Terminal Here") { run { $0.openTerminal() } }
            Button("Get Info") { run { $0.getInfo() } }
            Divider()
            Button("Move to Trash") { run { $0.trash() } }
        }
    }

    private func run(_ f: (WindowModel) -> Void) {
        window.focus(pane, select: urls.isEmpty ? nil : urls)
        if urls.isEmpty { pane.selectNone() }
        f(window)
    }
}

/// Return renames, Space is Quick Look, ⌘↓ opens, → / ← in columns.
private struct FileKeys: ViewModifier {
    @ObservedObject var pane: PaneModel
    @EnvironmentObject private var window: WindowModel

    func body(content: Content) -> some View {
        content
            .onKeyPress(.return) { window.focus(pane); window.rename(); return .handled }
            .onKeyPress(.space) { window.focus(pane); window.quickLook(); return .handled }
    }
}

extension View {
    fileprivate func fileKeys(_ pane: PaneModel) -> some View { modifier(FileKeys(pane: pane)) }
}

// MARK: - List

struct FileListView: View {
    @ObservedObject var pane: PaneModel
    @ObservedObject private var settings = AppSettings.shared
    @EnvironmentObject private var window: WindowModel
    @Environment(\.look) private var look
    @State private var sortOrder = [KeyPathComparator(\FileItem.name)]

    var body: some View {
        Table(of: FileItem.self, selection: $pane.selection, sortOrder: $sortOrder) {
            TableColumn("Name", value: \.name) { item in
                HStack(spacing: 7) {
                    FileIcon(url: item.url, size: settings.iconSize)
                    Text(item.displayName(showExtensions: settings.showExtensions)).lineLimit(1)
                    if !item.tags.isEmpty { TagDots(tags: item.tags) }
                }
                .opacity(FileOperations.shared.isCut(item.url) || item.isHidden ? 0.5 : 1)
            }
            .width(min: 180, ideal: 320)
            TableColumn("Date Modified", value: \.sortModified) { item in
                Text(Format.date(item.modified)).foregroundStyle(look.sub)
            }
            .width(min: 110, ideal: 160)
            TableColumn("Size", value: \.sortSize) { item in
                Text(Format.size(item, folderSize: pane.folderSizes[item.url])).foregroundStyle(look.sub)
            }
            .width(min: 60, ideal: 80)
            TableColumn("Kind", value: \.kindDescription) { item in
                Text(item.kindDescription).foregroundStyle(look.sub).lineLimit(1)
            }
            .width(min: 80, ideal: 130)
        } rows: {
            ForEach(pane.items) { item in
                TableRow(item)
                    .draggable(item.url)
                    .dropDestination(for: URL.self) { urls in
                        if item.isFolder { Drops.receive(urls, into: item.url, pane: pane) }
                    }
            }
        }
        .contextMenu(forSelectionType: URL.self) { sel in
            FileContextMenu(urls: Array(sel), pane: pane)
        } primaryAction: { sel in
            window.focus(pane, select: Array(sel))
            pane.openSelection()
        }
        .environment(\.defaultMinListRowHeight, settings.rowHeight)
        .scrollContentBackground(.hidden)
        .background(look.bg)
        .fileKeys(pane)
        .onChange(of: sortOrder) { _, new in
            guard let first = new.first else { return }
            let kp: AnyKeyPath = first.keyPath
            let key: SortKey
            if kp == \FileItem.sortModified { key = .modified }
            else if kp == \FileItem.sortSize { key = .size }
            else if kp == \FileItem.kindDescription { key = .kind }
            else { key = .name }
            pane.sort = FileSort(key: key, ascending: first.order == .forward)
        }
    }
}

extension FileItem {
    var sortModified: Date { modified ?? .distantPast }
    var sortSize: Int64 { size ?? -1 }
}

// MARK: - Grid

struct FileGridView: View {
    @ObservedObject var pane: PaneModel
    @ObservedObject private var settings = AppSettings.shared
    @EnvironmentObject private var window: WindowModel
    @Environment(\.look) private var look
    @State private var anchor: URL?

    private var cell: CGFloat { 96 * settings.zoom }

    var body: some View {
        ScrollView {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: cell + 16), spacing: 6)], spacing: 6) {
                ForEach(pane.items) { item in
                    GridCell(item: item, size: cell, selected: pane.selection.contains(item.url), showExt: settings.showExtensions)
                        .onTapGesture(count: 2) { window.focus(pane, select: [item.url]); pane.open(item) }
                        .onTapGesture { click(item) }
                        .contextMenu { FileContextMenu(urls: pane.selection.contains(item.url) ? pane.selectedURLs : [item.url], pane: pane) }
                        .draggable(item.url) { FileIcon(url: item.url, size: 48) }
                        .dropDestination(for: URL.self) { urls, _ in
                            guard item.isFolder else { return false }
                            Drops.receive(urls, into: item.url, pane: pane)
                            return true
                        }
                }
            }
            .padding(10)
        }
        .contentShape(Rectangle())
        .onTapGesture { pane.selectNone() }
        .contextMenu { FileContextMenu(urls: [], pane: pane) }
        .focusable()
        .focusEffectDisabled()
        .fileKeys(pane)
        .onKeyPress(.leftArrow) { step(-1) }
        .onKeyPress(.rightArrow) { step(1) }
    }

    private func click(_ item: FileItem) {
        window.focus(pane)
        let flags = NSEvent.modifierFlags
        if flags.contains(.command) {
            if pane.selection.contains(item.url) { pane.selection.remove(item.url) } else { pane.selection.insert(item.url) }
            anchor = item.url
        } else if flags.contains(.shift), let a = anchor, let i = pane.items.firstIndex(where: { $0.url == a }), let j = pane.items.firstIndex(of: item) {
            pane.selection = Set(pane.items[min(i, j)...max(i, j)].map(\.url))
        } else {
            pane.selection = [item.url]
            anchor = item.url
        }
    }

    private func step(_ by: Int) -> KeyPress.Result {
        guard !pane.items.isEmpty else { return .ignored }
        let current = pane.items.lastIndex { pane.selection.contains($0.url) } ?? -1
        let next = min(max(current + by, 0), pane.items.count - 1)
        pane.selection = [pane.items[next].url]
        anchor = pane.items[next].url
        return .handled
    }
}

private struct GridCell: View {
    let item: FileItem
    let size: CGFloat
    let selected: Bool
    let showExt: Bool
    @Environment(\.look) private var look

    var body: some View {
        VStack(spacing: 4) {
            Thumbnail(url: item.url, size: size)
            HStack(spacing: 3) {
                if !item.tags.isEmpty { TagDots(tags: item.tags) }
                Text(item.displayName(showExtensions: showExt))
                    .font(.system(size: 11))
                    .lineLimit(2)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 4).padding(.vertical, 1)
                    .background(RoundedRectangle(cornerRadius: 4).fill(selected ? look.accent : .clear))
                    .foregroundStyle(selected ? look.onAccent : look.text)
            }
        }
        .padding(6)
        .frame(width: size + 16)
        .background(RoundedRectangle(cornerRadius: 8).fill(selected ? look.accent.opacity(0.18) : .clear))
        .opacity(FileOperations.shared.isCut(item.url) || item.isHidden ? 0.5 : 1)
        .contentShape(Rectangle())
    }
}

// MARK: - Columns

struct ColumnsView: View {
    @ObservedObject var pane: PaneModel
    @Environment(\.look) private var look

    /// Up to three folders above the current one, then the current one.
    private var folders: [URL] { Array(PathTools.crumbs(pane.location).suffix(4)) }

    private var previewFolder: URL? {
        let sel = pane.selectedItems
        return sel.count == 1 && sel[0].isFolder ? sel[0].url : nil
    }

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.horizontal) {
                HStack(spacing: 0) {
                    ForEach(folders, id: \.self) { folder in
                        Group {
                            if folder == pane.location {
                                CurrentColumn(pane: pane)
                            } else {
                                StaticColumn(folder: folder, highlighted: next(after: folder), pane: pane)
                            }
                        }
                        .frame(width: 240)
                        Rectangle().fill(look.border).frame(width: 1)
                    }
                    if let f = previewFolder {
                        StaticColumn(folder: f, highlighted: nil, pane: pane).frame(width: 240).id("end")
                    } else {
                        Color.clear.frame(width: 1).id("end")
                    }
                }
            }
            .onChange(of: pane.location) { _, _ in withAnimation(.easeOut(duration: 0.15)) { proxy.scrollTo("end", anchor: .trailing) } }
            .onChange(of: previewFolder) { _, _ in proxy.scrollTo("end", anchor: .trailing) }
        }
    }

    private func next(after folder: URL) -> URL? {
        let all = PathTools.crumbs(pane.location)
        guard let i = all.firstIndex(of: folder), i + 1 < all.count else { return nil }
        return all[i + 1]
    }
}

/// The current folder's column: real selection, keyboard and right-click.
private struct CurrentColumn: View {
    @ObservedObject var pane: PaneModel
    @EnvironmentObject private var window: WindowModel
    @ObservedObject private var settings = AppSettings.shared

    var body: some View {
        List(pane.items, selection: $pane.selection) { item in
            ColumnRow(item: item, showExt: settings.showExtensions)
                .tag(item.url)
                .draggable(item.url)
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .contextMenu(forSelectionType: URL.self) { sel in
            FileContextMenu(urls: Array(sel), pane: pane)
        } primaryAction: { sel in
            window.focus(pane, select: Array(sel))
            pane.openSelection()
        }
        .fileKeys(pane)
        .onKeyPress(.rightArrow) {
            if let f = pane.selectedItems.first, f.isFolder, pane.selection.count == 1 { pane.go(to: f.url); return .handled }
            return .ignored
        }
        .onKeyPress(.leftArrow) { pane.goUp(); return .handled }
    }
}

/// A column for a folder above (or below) the current one.
private struct StaticColumn: View {
    let folder: URL
    let highlighted: URL?
    @ObservedObject var pane: PaneModel
    @ObservedObject private var settings = AppSettings.shared
    @Environment(\.look) private var look
    @State private var items: [FileItem] = []

    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 1) {
                ForEach(items) { item in
                    ColumnRow(item: item, showExt: settings.showExtensions)
                        .padding(.horizontal, 6)
                        .background(RoundedRectangle(cornerRadius: 5).fill(item.url == highlighted ? look.hover : .clear))
                        .contentShape(Rectangle())
                        .onTapGesture {
                            if item.isFolder { pane.go(to: item.url) } else { pane.go(to: folder, select: [item.url]) }
                        }
                        .draggable(item.url)
                }
            }
            .padding(4)
        }
        .task(id: folder) {
            let f = folder, hidden = settings.showHidden
            let list = await Task.detached { (try? PaneModel.list(f, showHidden: hidden)) ?? [] }.value
            items = Sorting.sort(list, by: FileSort(), foldersFirst: settings.foldersFirst)
        }
    }
}

private struct ColumnRow: View {
    let item: FileItem
    let showExt: Bool
    @Environment(\.look) private var look

    var body: some View {
        HStack(spacing: 6) {
            FileIcon(url: item.url, size: 16)
            Text(item.displayName(showExtensions: showExt)).lineLimit(1)
            Spacer(minLength: 4)
            if !item.tags.isEmpty { TagDots(tags: item.tags) }
            if item.isFolder { Image(systemName: "chevron.right").font(.system(size: 9, weight: .semibold)).foregroundStyle(look.sub) }
        }
        .frame(height: 22)
    }
}
