import Foundation
import AppKit
import KinetikFilesCore

enum ViewMode: String, CaseIterable, Identifiable, Codable {
    case list = "List", grid = "Grid", columns = "Columns"
    var id: String { rawValue }
    var symbol: String {
        switch self {
        case .list: "list.bullet"
        case .grid: "square.grid.2x2"
        case .columns: "rectangle.split.3x1"
        }
    }
}

/// One file pane: a folder, its history, its listing, the filter and the selection.
@MainActor
final class PaneModel: ObservableObject, Identifiable {
    let id = UUID()
    let settings = AppSettings.shared

    @Published private(set) var location: URL
    @Published private(set) var items: [FileItem] = []
    @Published var selection: Set<URL> = []
    @Published var viewMode: ViewMode = .list
    @Published var sort = FileSort() { didSet { refilter() } }
    @Published var filterText = "" { didSet { refilter() } }
    @Published var searchSubfolders = false { didSet { refilter() } }
    @Published private(set) var isLoading = false
    @Published private(set) var isSearching = false
    @Published private(set) var errorText: String?
    @Published private(set) var queryError: String?
    @Published private(set) var folderSizes: [URL: Int64] = [:]
    @Published private(set) var back: [URL] = []
    @Published private(set) var forward: [URL] = []

    private var allItems: [FileItem] = []
    private var watcher: DispatchSourceFileSystemObject?
    private var loadTask: Task<Void, Never>?
    private var searchTask: Task<Void, Never>?
    private var sizeTask: Task<Void, Never>?
    private var reloadPending = false

    init(location: URL) {
        // A file opens its folder with the file selected.
        let url = location.standardizedFileURL
        var isDir: ObjCBool = false
        let exists = FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir)
        if exists && !isDir.boolValue {
            self.location = url.deletingLastPathComponent()
            load(selecting: [url])
        } else {
            self.location = url
            load(selecting: [])
        }
    }

    var title: String {
        location.path == "/" ? (FileManager.default.displayName(atPath: "/")) : FileManager.default.displayName(atPath: location.path)
    }

    var selectedItems: [FileItem] { items.filter { selection.contains($0.url) } }
    var selectedURLs: [URL] { selectedItems.map(\.url) }
    var isFiltering: Bool { !filterText.trimmingCharacters(in: .whitespaces).isEmpty }

    // MARK: - Navigation

    func go(to url: URL, select: [URL] = [], recordHistory: Bool = true) {
        let target = url.standardizedFileURL
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: target.path, isDirectory: &isDir) else { return }
        if !isDir.boolValue {
            // A file: open its folder and select it.
            go(to: target.deletingLastPathComponent(), select: [target], recordHistory: recordHistory)
            return
        }
        if target != location {
            if recordHistory { back.append(location); forward.removeAll() }
            location = target
            filterText = ""
            selection = []
        }
        load(selecting: select)
    }

    func goBack() {
        guard let prev = back.popLast() else { return }
        let from = location
        forward.append(from)
        go(to: prev, select: PathTools.isInside(from, prev) ? [childOf(prev, toward: from)] : [], recordHistory: false)
    }

    func goForward() {
        guard let next = forward.popLast() else { return }
        back.append(location)
        go(to: next, recordHistory: false)
    }

    func goUp() {
        let parent = location.deletingLastPathComponent().standardizedFileURL
        guard parent.path != location.path else { return }
        go(to: parent, select: [location])
    }

    /// Opens an item: folders open here, everything else opens in its app.
    func open(_ item: FileItem) {
        if item.isFolder {
            go(to: item.url)
        } else if item.isAlias, let target = try? URL(resolvingAliasFileAt: item.url) {
            go(to: target)
        } else {
            NSWorkspace.shared.open(item.url)
        }
    }

    func openSelection() {
        let sel = selectedItems
        if sel.count == 1 { open(sel[0]); return }
        for item in sel where !item.isFolder { NSWorkspace.shared.open(item.url) }
    }

    private func childOf(_ folder: URL, toward deep: URL) -> URL {
        let f = folder.standardizedFileURL.pathComponents, d = deep.standardizedFileURL.pathComponents
        guard d.count > f.count else { return deep }
        return folder.appendingPathComponent(d[f.count])
    }

    // MARK: - Loading

    func reload() { load(selecting: Array(selection)) }

    func load(selecting select: [URL]) {
        loadTask?.cancel()
        isLoading = true
        errorText = nil
        let url = location, hidden = settings.showHidden
        loadTask = Task { [weak self] in
            let result: Result<[FileItem], Error> = await Task.detached(priority: .userInitiated) { () -> Result<[FileItem], Error> in
                do { return .success(try PaneModel.list(url, showHidden: hidden)) } catch { return .failure(error) }
            }.value
            guard let self, !Task.isCancelled, url == self.location else { return }
            self.isLoading = false
            switch result {
            case .success(let list):
                self.allItems = list
                self.refilter()
                let keep = Set(select.map(\.standardizedFileURL)).intersection(self.items.map(\.url))
                if !select.isEmpty { self.selection = keep }
                else { self.selection = self.selection.intersection(Set(self.items.map(\.url))) }
                self.watch()
                self.calculateFolderSizes()
            case .failure(let error):
                self.allItems = []; self.items = []
                self.errorText = PaneModel.describe(error, url)
            }
        }
    }

    nonisolated static func list(_ url: URL, showHidden: Bool) throws -> [FileItem] {
        let urls = try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: FileItem.keys, options: showHidden ? [] : [.skipsHiddenFiles])
        return urls.compactMap { FileItem(url: $0) }.filter { showHidden || !$0.isHidden }
    }

    static func describe(_ error: Error, _ url: URL) -> String {
        let ns = error as NSError
        if ns.domain == NSCocoaErrorDomain && (ns.code == NSFileReadNoPermissionError || ns.code == 257) {
            return "macOS hasn't given Kinetik Files permission to see \(url.lastPathComponent). Turn on Full Disk Access for Kinetik Files in System Settings → Privacy & Security."
        }
        return ns.localizedDescription
    }

    /// Re-applies the filter and sort to the listing (or starts a search).
    func refilter() {
        searchTask?.cancel()
        isSearching = false
        let query = Query.parse(filterText)
        queryError = query.error
        if query.isEmpty {
            items = Sorting.sort(allItems.map(withSize), by: sort, foldersFirst: settings.foldersFirst)
            return
        }
        if searchSubfolders || !query.contentTerms.isEmpty {
            startSearch()
            return
        }
        items = Sorting.sort(allItems.map(withSize).filter { query.matches($0.facts) }, by: sort, foldersFirst: settings.foldersFirst)
    }

    private func withSize(_ item: FileItem) -> FileItem {
        guard item.isFolder, let s = folderSizes[item.url] else { return item }
        var i = item; i.size = s; return i
    }

    // MARK: - Search

    private func startSearch() {
        items = []
        isSearching = true
        let root = location, deep = searchSubfolders, hidden = settings.showHidden, order = sort, foldersFirst = settings.foldersFirst
        let text = filterText
        searchTask = Task { [weak self] in
            let stream = AsyncStream<[FileItem]> { continuation in
                let worker = Task.detached(priority: .userInitiated) {
                    PaneModel.search(root: root, deep: deep, showHidden: hidden, text: text) { batch in continuation.yield(batch) }
                    continuation.finish()
                }
                continuation.onTermination = { _ in worker.cancel() }
            }
            var found: [FileItem] = []
            for await batch in stream {
                guard let self, !Task.isCancelled else { return }
                found += batch
                self.items = Sorting.sort(found, by: order, foldersFirst: foldersFirst)
            }
            self?.isSearching = false
        }
    }

    /// Walks `root` (and its subfolders when `deep`), sending matches in batches. Runs off the main thread.
    nonisolated static func search(root: URL, deep: Bool, showHidden: Bool, text: String, send: ([FileItem]) -> Void) {
        let query = Query.parse(text)
        var options: FileManager.DirectoryEnumerationOptions = [.skipsPackageDescendants]
        if !showHidden { options.insert(.skipsHiddenFiles) }
        if !deep { options.insert(.skipsSubdirectoryDescendants) }
        guard let e = FileManager.default.enumerator(at: root, includingPropertiesForKeys: FileItem.keys, options: options) else { return }
        var batch: [FileItem] = []
        var last = Date()
        for case let url as URL in e {
            if Task.isCancelled { return }
            guard let item = FileItem(url: url) else { continue }
            let ok = query.matches(item.facts) { needle in
                guard !item.isFolder else { return false }
                return ContentSearch.contains(url, needle)
            }
            if ok { batch.append(item) }
            if !batch.isEmpty && (batch.count >= 200 || Date().timeIntervalSince(last) > 0.25) {
                send(batch); batch = []; last = Date()
            }
        }
        if !batch.isEmpty { send(batch) }
    }

    // MARK: - Watching and folder sizes

    private func watch() {
        watcher?.cancel()
        watcher = nil
        let fd = Darwin.open(location.path, O_EVTONLY)
        guard fd >= 0 else { return }
        let src = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename, .delete, .extend, .attrib], queue: .main)
        src.setEventHandler { [weak self] in
            Task { @MainActor in self?.scheduleReload() }
        }
        src.setCancelHandler { Darwin.close(fd) }
        src.resume()
        watcher = src
    }

    private func scheduleReload() {
        guard !reloadPending else { return }
        reloadPending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in
            Task { @MainActor in
                guard let self else { return }
                self.reloadPending = false
                if self.searchTask == nil || !self.isSearching { self.reload() }
            }
        }
    }

    private func calculateFolderSizes() {
        sizeTask?.cancel()
        guard settings.calculateFolderSizes else { return }
        let folders = allItems.filter(\.isFolder).map(\.url).filter { folderSizes[$0] == nil }
        guard !folders.isEmpty else { return }
        sizeTask = Task { [weak self] in
            for folder in folders {
                if Task.isCancelled { return }
                let size = await FolderSizes.shared.size(of: folder)
                guard let self, !Task.isCancelled else { return }
                self.folderSizes[folder] = size
            }
            if self?.sort.key == .size { self?.refilter() }
        }
    }

    /// The pane is going away (its tab closed).
    func close() {
        watcher?.cancel(); watcher = nil
        loadTask?.cancel(); searchTask?.cancel(); sizeTask?.cancel()
    }

    // MARK: - Selection helpers

    func selectAll() { selection = Set(items.map(\.url)) }
    func selectNone() { selection = [] }
    func invertSelection() { selection = Set(items.map(\.url)).subtracting(selection) }
    func select(_ urls: [URL]) { selection = Set(urls.map(\.standardizedFileURL)) }
}

/// Folder sizes, worked out in the background and remembered until the folder changes.
actor FolderSizes {
    static let shared = FolderSizes()
    private var cache: [URL: (size: Int64, modified: Date?)] = [:]

    func size(of folder: URL) async -> Int64 {
        let modified = (try? folder.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate
        if let c = cache[folder], c.modified == modified { return c.size }
        let s = await Task.detached(priority: .utility) { FolderSizes.measure(folder) }.value
        cache[folder] = (s, modified)
        return s
    }

    func forget(_ folder: URL) { cache[folder] = nil }

    nonisolated static func measure(_ folder: URL) -> Int64 {
        let keys: [URLResourceKey] = [.totalFileAllocatedSizeKey, .fileSizeKey, .isRegularFileKey]
        guard let e = FileManager.default.enumerator(at: folder, includingPropertiesForKeys: keys, options: [], errorHandler: { _, _ in true }) else { return 0 }
        var total: Int64 = 0
        for case let url as URL in e {
            if Task.isCancelled { break }
            guard let v = try? url.resourceValues(forKeys: Set(keys)), v.isRegularFile == true else { continue }
            total += Int64(v.totalFileAllocatedSize ?? v.fileSize ?? 0)
        }
        return total
    }
}

/// Searching inside files for content: terms.
enum ContentSearch {
    static let textLike: Set<String> = Set(Kinds.map["code"] ?? []).union(["txt", "md", "csv", "log", "rtf", "html", "xml", "json", "ini", "cfg", "conf", "plist", "tex", "srt", "vtt"])

    static func contains(_ url: URL, _ needle: String) -> Bool {
        let ext = url.pathExtension.lowercased()
        guard textLike.contains(ext) || ext.isEmpty else { return false }
        guard let v = try? url.resourceValues(forKeys: [.fileSizeKey]), (v.fileSize ?? 0) < 32_000_000 else { return false }
        guard let data = try? Data(contentsOf: url, options: .mappedIfSafe) else { return false }
        let text = String(decoding: data, as: UTF8.self)
        return text.range(of: needle, options: [.caseInsensitive, .diacriticInsensitive]) != nil
    }
}
