import Foundation
import AppKit
import KinetikFilesCore

/// Every change Kinetik Files makes to files goes through here, so it can be undone, reported and kept off the main thread.
@MainActor
final class FileOperations: ObservableObject {
    static let shared = FileOperations()

    @Published private(set) var history = UndoHistory(limit: 100)
    /// What's happening right now, for the status bar ("Copying 3 items…").
    @Published private(set) var busy: String?
    /// A problem to show the person.
    @Published var problem: String?

    // Cut: Finder has no cut for files, so we remember what was cut and move it on paste.
    private var cutURLs: [URL] = []
    private var cutChangeCount = -1

    // MARK: - Copy, move, paste

    func copy(_ urls: [URL], into folder: URL, title: String? = nil) async -> [URL] {
        await transfer(urls, into: folder, move: false, title: title ?? "Copy \(Self.count(urls))")
    }

    func move(_ urls: [URL], into folder: URL) async -> [URL] {
        await transfer(urls, into: folder, move: true, title: "Move \(Self.count(urls))")
    }

    private func transfer(_ urls: [URL], into folder: URL, move: Bool, title: String) async -> [URL] {
        let sources = urls.filter { !(move && $0.deletingLastPathComponent().standardizedFileURL == folder.standardizedFileURL) }
        guard !sources.isEmpty else { return [] }
        if let bad = sources.first(where: { PathTools.isInside(folder, $0) }) {
            problem = "Can't put “\(bad.lastPathComponent)” inside itself."
            return []
        }
        busy = (move ? "Moving " : "Copying ") + Self.count(sources) + "…"
        let result = await Task.detached(priority: .userInitiated) { () -> ([FileChange], [URL], [String]) in
            let fm = FileManager.default
            var changes: [FileChange] = [], made: [URL] = [], errors: [String] = []
            for src in sources {
                let sameFolder = src.deletingLastPathComponent().standardizedFileURL == folder.standardizedFileURL
                let name = PathTools.uniqueName(src.lastPathComponent, in: folder, copySuffix: sameFolder && !move) { fm.fileExists(atPath: $0.path) }
                let dst = folder.appendingPathComponent(name)
                do {
                    if move {
                        try fm.moveItem(at: src, to: dst)
                        changes.append(.moved(from: src, to: dst))
                    } else {
                        try fm.copyItem(at: src, to: dst) // clones instantly on APFS
                        changes.append(.created(dst))
                    }
                    made.append(dst)
                } catch {
                    errors.append("\(src.lastPathComponent): \(error.localizedDescription)")
                }
            }
            return (changes, made, errors)
        }.value
        busy = nil
        history.record(UndoEntry(title, result.0))
        if !result.2.isEmpty { problem = result.2.joined(separator: "\n") }
        return result.1
    }

    func duplicate(_ urls: [URL]) async -> [URL] {
        guard let folder = urls.first?.deletingLastPathComponent() else { return [] }
        return await copy(urls, into: folder, title: "Duplicate \(Self.count(urls))")
    }

    func copyToPasteboard(_ urls: [URL], cut: Bool) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects(urls.map { $0 as NSURL })
        pb.setString(urls.map(\.path).joined(separator: "\n"), forType: .string)
        cutURLs = cut ? urls : []
        cutChangeCount = cut ? pb.changeCount : -1
    }

    var pasteboardHasFiles: Bool { !pasteboardURLs().isEmpty }

    func pasteboardURLs() -> [URL] {
        (NSPasteboard.general.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
    }

    /// Paste into `folder`. Moves if the files were cut here (or `forceMove`, ⌘⌥V like Finder), otherwise copies.
    func paste(into folder: URL, forceMove: Bool = false) async -> [URL] {
        let urls = pasteboardURLs()
        if urls.isEmpty { return await pasteContentAsFile(into: folder) }
        let isCut = NSPasteboard.general.changeCount == cutChangeCount && Set(urls) == Set(cutURLs)
        if isCut || forceMove {
            let made = await move(urls, into: folder)
            if isCut { cutURLs = []; cutChangeCount = -1 }
            return made
        }
        return await copy(urls, into: folder, title: "Paste \(Self.count(urls))")
    }

    func isCut(_ url: URL) -> Bool {
        NSPasteboard.general.changeCount == cutChangeCount && cutURLs.contains(url)
    }

    /// ⌘⇧V: a copied image or text becomes a new file.
    func pasteContentAsFile(into folder: URL) async -> [URL] {
        let pb = NSPasteboard.general
        let stamp: String = {
            let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"; return f.string(from: Date())
        }()
        let fm = FileManager.default
        var data: Data?
        var name = ""
        if let image = NSImage(pasteboard: pb), let tiff = image.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff),
           let png = rep.representation(using: .png, properties: [:]) {
            data = png; name = "Pasted image \(stamp).png"
        } else if let text = pb.string(forType: .string), !text.isEmpty {
            data = Data(text.utf8); name = "Pasted text \(stamp).txt"
        }
        guard let data else { problem = "There's nothing to paste."; return [] }
        let dst = folder.appendingPathComponent(PathTools.uniqueName(name, in: folder) { fm.fileExists(atPath: $0.path) })
        do {
            try data.write(to: dst, options: .withoutOverwriting)
            history.record(UndoEntry("Paste as File", [.created(dst)]))
            return [dst]
        } catch {
            problem = error.localizedDescription
            return []
        }
    }

    // MARK: - Create

    func newFolder(in folder: URL, name: String = "untitled folder") -> URL? {
        let fm = FileManager.default
        let dst = folder.appendingPathComponent(PathTools.uniqueName(name, in: folder) { fm.fileExists(atPath: $0.path) })
        do {
            try fm.createDirectory(at: dst, withIntermediateDirectories: false)
            history.record(UndoEntry("New Folder", [.created(dst)]))
            return dst
        } catch { problem = error.localizedDescription; return nil }
    }

    func newFolder(with urls: [URL]) async -> URL? {
        guard let parent = urls.first?.deletingLastPathComponent(), let folder = newFolder(in: parent, name: "New Folder With Items") else { return nil }
        _ = await move(urls, into: folder)
        return folder
    }

    func newTextFile(in folder: URL) -> URL? {
        let fm = FileManager.default
        let dst = folder.appendingPathComponent(PathTools.uniqueName("untitled.txt", in: folder) { fm.fileExists(atPath: $0.path) })
        guard fm.createFile(atPath: dst.path, contents: Data()) else { problem = "Couldn't make a text file here."; return nil }
        history.record(UndoEntry("New Text File", [.created(dst)]))
        return dst
    }

    // MARK: - Rename

    @discardableResult
    func rename(_ url: URL, to newName: String) -> URL? {
        let name = newName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name != url.lastPathComponent else { return url }
        if name.contains("/") || name.contains(":") { problem = "Names can't contain / or :"; return nil }
        let dst = url.deletingLastPathComponent().appendingPathComponent(name)
        let fm = FileManager.default
        let caseOnly = name.lowercased() == url.lastPathComponent.lowercased()
        if !caseOnly && fm.fileExists(atPath: dst.path) { problem = "Something called “\(name)” is already here."; return nil }
        do {
            if caseOnly {
                // Case-only renames on a case-insensitive disk need a stop in between.
                let temp = url.deletingLastPathComponent().appendingPathComponent(".kinetik-\(UUID().uuidString)")
                try fm.moveItem(at: url, to: temp)
                try fm.moveItem(at: temp, to: dst)
            } else {
                try fm.moveItem(at: url, to: dst)
            }
            history.record(UndoEntry("Rename", [.moved(from: url, to: dst)]))
            return dst
        } catch { problem = error.localizedDescription; return nil }
    }

    /// Performs a batch rename plan (see BatchRename.moves). If anything fails, everything done so far is put back.
    func batchRename(_ plan: [RenamePlanEntry]) -> Bool {
        guard let moves = BatchRename.moves(for: plan) else { problem = "Some names have problems."; return false }
        let fm = FileManager.default
        var done: [(from: URL, to: URL)] = []
        do {
            for m in moves {
                try fm.moveItem(at: m.from, to: m.to)
                done.append(m)
            }
        } catch {
            for m in done.reversed() { try? fm.moveItem(at: m.to, to: m.from) }
            problem = "Nothing was renamed: \(error.localizedDescription)"
            return false
        }
        let changes = plan.filter(\.changed).map { FileChange.moved(from: $0.source, to: $0.target) }
        history.record(UndoEntry("Rename \(Self.count(changes.count))", changes))
        return true
    }

    // MARK: - Trash and delete

    func trash(_ urls: [URL]) async {
        guard !urls.isEmpty else { return }
        busy = "Moving \(Self.count(urls)) to the Trash…"
        let result = await Task.detached { () -> ([FileChange], [String]) in
            var changes: [FileChange] = [], errors: [String] = []
            for u in urls {
                var out: NSURL?
                do {
                    try FileManager.default.trashItem(at: u, resultingItemURL: &out)
                    if let t = out as URL? { changes.append(.trashed(original: u, inTrash: t)) }
                } catch { errors.append("\(u.lastPathComponent): \(error.localizedDescription)") }
            }
            return (changes, errors)
        }.value
        busy = nil
        history.record(UndoEntry("Move to Trash", result.0))
        if !result.1.isEmpty { problem = result.1.joined(separator: "\n") }
    }

    /// Deletes for good (can't be undone). The caller confirms first.
    func deleteImmediately(_ urls: [URL]) async {
        busy = "Deleting \(Self.count(urls))…"
        let errors = await Task.detached { () -> [String] in
            urls.compactMap { u in
                do { try FileManager.default.removeItem(at: u); return nil } catch { return "\(u.lastPathComponent): \(error.localizedDescription)" }
            }
        }.value
        busy = nil
        if !errors.isEmpty { problem = errors.joined(separator: "\n") }
    }

    // MARK: - Tags

    func setTags(_ urls: [URL], add: String? = nil, remove: String? = nil, replaceWith: [String]? = nil) {
        var changes: [FileChange] = []
        for u in urls {
            let old = (try? u.resourceValues(forKeys: [.tagNamesKey]))?.tagNames ?? []
            var new = replaceWith ?? old
            if let add, !new.contains(add) { new.append(add) }
            if let remove { new.removeAll { $0 == remove } }
            guard new != old else { continue }
            if Self.writeTags(u, new) { changes.append(.tagged(u, old: old, new: new)) }
        }
        history.record(UndoEntry("Tags", changes))
    }

    nonisolated static func writeTags(_ url: URL, _ tags: [String]) -> Bool {
        do {
            try (url as NSURL).setResourceValue(tags, forKey: .tagNamesKey)
            return true
        } catch { return false }
    }

    // MARK: - Zip

    func compress(_ urls: [URL]) async -> URL? {
        guard let folder = urls.first?.deletingLastPathComponent() else { return nil }
        let fm = FileManager.default
        let base = urls.count == 1 ? urls[0].deletingPathExtension().lastPathComponent + ".zip" : "Archive.zip"
        let dst = folder.appendingPathComponent(PathTools.uniqueName(base, in: folder) { fm.fileExists(atPath: $0.path) })
        busy = "Compressing \(Self.count(urls))…"
        var ok: Bool
        if urls.count == 1 {
            ok = await Self.run("/usr/bin/ditto", ["-c", "-k", "--sequesterRsrc", "--keepParent", urls[0].path, dst.path])
        } else {
            // ditto takes one source, so stage the items in a temporary folder of links first.
            let stage = fm.temporaryDirectory.appendingPathComponent("kinetik-zip-\(UUID().uuidString)")
            try? fm.createDirectory(at: stage, withIntermediateDirectories: true)
            defer { try? fm.removeItem(at: stage) }
            for u in urls { try? fm.copyItem(at: u, to: stage.appendingPathComponent(u.lastPathComponent)) }
            ok = await Self.run("/usr/bin/ditto", ["-c", "-k", "--sequesterRsrc", stage.path, dst.path])
        }
        busy = nil
        if ok { history.record(UndoEntry("Compress", [.created(dst)])); return dst }
        problem = "Couldn't make the zip file."
        return nil
    }

    /// Extracts into a new folder next to the zip, never over existing files.
    func extract(_ zip: URL) async -> URL? {
        let fm = FileManager.default
        let folder = zip.deletingLastPathComponent()
        let dst = folder.appendingPathComponent(PathTools.uniqueName(zip.deletingPathExtension().lastPathComponent, in: folder) { fm.fileExists(atPath: $0.path) })
        busy = "Extracting \(zip.lastPathComponent)…"
        let ok = await Self.run("/usr/bin/ditto", ["-x", "-k", zip.path, dst.path])
        busy = nil
        guard ok else { problem = "Couldn't extract \(zip.lastPathComponent)."; return nil }
        // If the zip held a single folder, lift it out instead of nesting folder/folder.
        if let inner = try? fm.contentsOfDirectory(at: dst, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]), inner.count == 1,
           (try? inner[0].resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true {
            let lifted = folder.appendingPathComponent(PathTools.uniqueName(inner[0].lastPathComponent, in: folder) { fm.fileExists(atPath: $0.path) })
            if (try? fm.moveItem(at: inner[0], to: lifted)) != nil {
                try? fm.removeItem(at: dst)
                history.record(UndoEntry("Extract", [.created(lifted)]))
                return lifted
            }
        }
        history.record(UndoEntry("Extract", [.created(dst)]))
        return dst
    }

    nonisolated static func run(_ tool: String, _ args: [String]) async -> Bool {
        await withCheckedContinuation { cont in
            let p = Process()
            p.executableURL = URL(fileURLWithPath: tool)
            p.arguments = args
            p.standardOutput = FileHandle.nullDevice
            p.standardError = FileHandle.nullDevice
            p.terminationHandler = { cont.resume(returning: $0.terminationStatus == 0) }
            do { try p.run() } catch { cont.resume(returning: false) }
        }
    }

    // MARK: - Undo and redo

    func undo() async {
        guard let entry = history.takeUndo() else { return }
        let done = await reverse(entry)
        history.pushRedo(UndoEntry(entry.title, done))
    }

    func redo() async {
        guard let entry = history.takeRedo() else { return }
        let done = await reverse(entry)
        history.pushUndo(UndoEntry(entry.title, done))
    }

    /// Reverses each change, newest first, and returns what was actually done (so it can be reversed again).
    private func reverse(_ entry: UndoEntry) async -> [FileChange] {
        busy = "Undoing \(entry.title)…"
        let result = await Task.detached { () -> ([FileChange], [String]) in
            let fm = FileManager.default
            var done: [FileChange] = [], errors: [String] = []
            for change in entry.changes.reversed() {
                do {
                    switch change {
                    case .moved(let from, let to):
                        if fm.fileExists(atPath: from.path) && from.path.lowercased() != to.path.lowercased() {
                            throw CocoaError(.fileWriteFileExists)
                        }
                        try fm.moveItem(at: to, to: from)
                        done.append(.moved(from: to, to: from))
                    case .created(let url):
                        var out: NSURL?
                        try fm.trashItem(at: url, resultingItemURL: &out)
                        if let t = out as URL? { done.append(.trashed(original: url, inTrash: t)) }
                    case .trashed(let original, let inTrash):
                        if fm.fileExists(atPath: original.path) { throw CocoaError(.fileWriteFileExists) }
                        try fm.moveItem(at: inTrash, to: original)
                        done.append(.created(original))
                    case .tagged(let url, let old, let new):
                        if FileOperations.writeTags(url, old) { done.append(.tagged(url, old: new, new: old)) }
                    }
                } catch {
                    errors.append(error.localizedDescription)
                }
            }
            // Already newest-first, which is the order a later reverse needs.
            return (done, errors)
        }.value
        busy = nil
        if !result.1.isEmpty { problem = "Some changes couldn't be undone:\n" + result.1.joined(separator: "\n") }
        return result.0
    }

    // MARK: - Helpers

    static func count(_ urls: [URL]) -> String { count(urls.count) }
    static func count(_ n: Int) -> String { n == 1 ? "1 item" : "\(n) items" }
}
