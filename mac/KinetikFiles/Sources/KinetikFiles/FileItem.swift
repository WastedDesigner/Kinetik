import Foundation
import AppKit
import KinetikFilesCore

/// One file or folder in a listing.
struct FileItem: Identifiable, Hashable {
    let url: URL
    var name: String
    var isFolder: Bool       // a real folder you can open (not a package like .app)
    var isPackage: Bool
    var isHidden: Bool
    var isAlias: Bool
    var size: Int64?         // files: their size; folders: filled in by the folder-size calculator
    var modified: Date?
    var created: Date?
    var added: Date?
    var tags: [String]
    var kindDescription: String

    var id: URL { url }

    static let keys: [URLResourceKey] = [
        .nameKey, .isDirectoryKey, .isPackageKey, .isHiddenKey, .isAliasFileKey, .isSymbolicLinkKey,
        .fileSizeKey, .totalFileAllocatedSizeKey, .contentModificationDateKey, .creationDateKey,
        .addedToDirectoryDateKey, .tagNamesKey, .localizedTypeDescriptionKey,
    ]

    init?(url: URL) {
        guard let v = try? url.resourceValues(forKeys: Set(FileItem.keys)) else { return nil }
        self.url = url
        name = v.name ?? url.lastPathComponent
        isPackage = v.isPackage ?? false
        isAlias = (v.isAliasFile ?? false) && !(v.isSymbolicLink ?? false)
        var dir = v.isDirectory ?? false
        // A symlink to a folder opens like a folder.
        if v.isSymbolicLink == true, let target = try? url.resolvingSymlinksInPath().resourceValues(forKeys: [.isDirectoryKey]) {
            dir = target.isDirectory ?? false
        }
        isFolder = dir && !isPackage
        isHidden = v.isHidden ?? name.hasPrefix(".")
        size = isFolder ? nil : Int64(v.fileSize ?? v.totalFileAllocatedSize ?? 0)
        modified = v.contentModificationDate
        created = v.creationDate
        added = v.addedToDirectoryDate
        tags = v.tagNames ?? []
        kindDescription = v.localizedTypeDescription ?? (isFolder ? "Folder" : "Document")
    }

    var facts: FileFacts {
        FileFacts(name: name, isFolder: isFolder || isPackage, size: size, modified: modified, created: created, added: added, tags: tags)
    }

    var ext: String { isFolder ? "" : url.pathExtension.lowercased() }

    func displayName(showExtensions: Bool) -> String {
        guard !showExtensions, !isFolder, !url.pathExtension.isEmpty, name.first != "." else { return name }
        return url.deletingPathExtension().lastPathComponent
    }
}

enum SortKey: String, CaseIterable, Identifiable, Codable {
    case name = "Name", modified = "Date Modified", created = "Date Created", added = "Date Added", size = "Size", kind = "Kind"
    var id: String { rawValue }
}

struct FileSort: Equatable, Codable {
    var key: SortKey = .name
    var ascending = true
}

enum Sorting {
    static func sort(_ items: [FileItem], by order: FileSort, foldersFirst: Bool) -> [FileItem] {
        items.sorted { a, b in
            if foldersFirst && a.isFolder != b.isFolder { return a.isFolder }
            let r: ComparisonResult
            switch order.key {
            case .name: r = a.name.localizedStandardCompare(b.name)
            case .modified: r = compare(a.modified, b.modified)
            case .created: r = compare(a.created, b.created)
            case .added: r = compare(a.added, b.added)
            case .size: r = compare(a.size ?? -1, b.size ?? -1)
            case .kind: r = a.kindDescription.localizedStandardCompare(b.kindDescription)
            }
            if r == .orderedSame { return a.name.localizedStandardCompare(b.name) == .orderedAscending }
            return order.ascending ? r == .orderedAscending : r == .orderedDescending
        }
    }

    private static func compare<T: Comparable>(_ a: T?, _ b: T?) -> ComparisonResult {
        switch (a, b) {
        case let (x?, y?): return x < y ? .orderedAscending : x > y ? .orderedDescending : .orderedSame
        case (nil, nil): return .orderedSame
        case (nil, _): return .orderedAscending
        case (_, nil): return .orderedDescending
        }
    }

    private static func compare(_ a: Int64, _ b: Int64) -> ComparisonResult {
        a < b ? .orderedAscending : a > b ? .orderedDescending : .orderedSame
    }
}

/// File icons and Quick Look thumbnails, cached.
@MainActor
final class Icons {
    static let shared = Icons()
    private let cache = NSCache<NSString, NSImage>()

    func icon(for url: URL) -> NSImage {
        let key = url.path as NSString
        if let i = cache.object(forKey: key) { return i }
        let i = NSWorkspace.shared.icon(forFile: url.path)
        cache.setObject(i, forKey: key)
        return i
    }
}
