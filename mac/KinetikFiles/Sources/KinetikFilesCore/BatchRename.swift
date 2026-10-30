import Foundation

// Batch rename, the same rules as the Windows app: a pattern with tokens, then find and replace (plain or regular
// expression), then a case change. Everything here is pure: `plan` works out new names and problems, `moves` turns a
// plan into the actual renames (via temporary names, so swapping "a" and "b" works), and the app performs them.

public enum CaseChange: String, CaseIterable, Codable, Sendable {
    case none = "Keep"
    case lower = "lowercase"
    case upper = "UPPERCASE"
    case title = "Title Case"
    case sentence = "Sentence case"
}

public struct RenameOptions: Equatable, Sendable {
    /// The new name. Tokens: [Name] [Ext] [N] [N:3] (zero-padded) [Parent] [Date] [Date:yyyy-MM-dd] [Created:…].
    /// Empty means "keep the current name" (find/replace and case still apply).
    public var pattern: String = ""
    public var find: String = ""
    public var replace: String = ""
    public var useRegex = false
    public var matchCase = false
    public var caseChange: CaseChange = .none
    public var startNumber = 1
    public var step = 1
    /// When on, the pattern, find/replace and case change only touch the name, never the extension.
    public var keepExtension = true

    public init() {}
}

public struct RenameItem: Sendable {
    public let url: URL
    public let modified: Date?
    public let created: Date?
    public init(url: URL, modified: Date? = nil, created: Date? = nil) {
        self.url = url; self.modified = modified; self.created = created
    }
}

public struct RenamePlanEntry: Equatable, Sendable {
    public let source: URL
    public let newName: String
    public let problem: String?
    public var changed: Bool { newName != source.lastPathComponent }
    public var target: URL { source.deletingLastPathComponent().appendingPathComponent(newName) }
}

public enum BatchRename {
    /// Works out every new name. `exists` reports whether a path is already taken on disk (by something not being renamed).
    public static func plan(_ items: [RenameItem], _ o: RenameOptions, exists: (URL) -> Bool = { _ in false }) -> [RenamePlanEntry] {
        var regex: NSRegularExpression? = nil
        var regexError: String? = nil
        if o.useRegex && !o.find.isEmpty {
            do { regex = try NSRegularExpression(pattern: o.find, options: o.matchCase ? [] : [.caseInsensitive]) }
            catch { regexError = "The regular expression isn't valid" }
        }

        var names: [String] = []
        for (i, item) in items.enumerated() {
            let original = item.url.lastPathComponent
            let (base, ext) = split(original, keepExtension: o.keepExtension, isFolder: item.url.hasDirectoryPath)
            var name = o.pattern.isEmpty ? base : expand(o.pattern, base: base, ext: ext, index: o.startNumber + i * o.step, item: item)
            if !o.find.isEmpty {
                if let regex {
                    let range = NSRange(name.startIndex..., in: name)
                    name = regex.stringByReplacingMatches(in: name, range: range, withTemplate: o.replace)
                } else if regexError == nil {
                    name = name.replacingOccurrences(of: o.find, with: o.replace, options: o.matchCase ? [] : [.caseInsensitive])
                }
            }
            name = applyCase(name, o.caseChange)
            names.append(ext.isEmpty ? name : name + "." + ext)
        }

        let sources = Set(items.map { $0.url.standardizedFileURL.path.lowercased() })
        var seen: [String: Int] = [:]
        for (i, n) in names.enumerated() {
            let key = items[i].url.deletingLastPathComponent().appendingPathComponent(n).standardizedFileURL.path.lowercased()
            seen[key, default: 0] += 1
        }

        return items.enumerated().map { i, item in
            let n = names[i]
            let target = item.url.deletingLastPathComponent().appendingPathComponent(n)
            let key = target.standardizedFileURL.path.lowercased()
            var problem: String? = regexError
            if problem == nil {
                if n.trimmingCharacters(in: .whitespaces).isEmpty || n == "." || n == ".." { problem = "Empty name" }
                else if n.contains("/") || n.contains(":") { problem = "Names can't contain / or :" }
                else if n.utf8.count > 255 { problem = "Name is too long" }
                else if (seen[key] ?? 0) > 1 { problem = "Two items would get this name" }
                else if n != item.url.lastPathComponent && !sources.contains(key) && exists(target) { problem = "Something with this name is already here" }
            }
            return RenamePlanEntry(source: item.url, newName: n, problem: problem)
        }
    }

    /// The renames to perform, in order. Items that only change case, or that take a name another item is giving up,
    /// go through a temporary name first. Returns nil if any entry has a problem.
    public static func moves(for plan: [RenamePlanEntry], token: String = UUID().uuidString.prefix(8).lowercased()) -> [(from: URL, to: URL)]? {
        if plan.contains(where: { $0.problem != nil }) { return nil }
        let changing = plan.filter { $0.changed }
        var first: [(URL, URL)] = [], second: [(URL, URL)] = []
        for (i, e) in changing.enumerated() {
            let temp = e.source.deletingLastPathComponent().appendingPathComponent(".kinetik-rename-\(token)-\(i)")
            first.append((e.source, temp))
            second.append((temp, e.target))
        }
        return first.map { (from: $0.0, to: $0.1) } + second.map { (from: $0.0, to: $0.1) }
    }

    // MARK: - Pieces

    static func split(_ name: String, keepExtension: Bool, isFolder: Bool) -> (String, String) {
        guard keepExtension, !isFolder, let dot = name.lastIndex(of: "."), dot != name.startIndex else { return (name, "") }
        let ext = String(name[name.index(after: dot)...])
        if ext.isEmpty || ext.contains(" ") { return (name, "") }
        return (String(name[..<dot]), ext)
    }

    static func expand(_ pattern: String, base: String, ext: String, index: Int, item: RenameItem) -> String {
        var out = ""
        var i = pattern.startIndex
        while i < pattern.endIndex {
            if pattern[i] == "[", let close = pattern[i...].firstIndex(of: "]") {
                let token = String(pattern[pattern.index(after: i)..<close])
                if let value = tokenValue(token, base: base, ext: ext, index: index, item: item) {
                    out += value
                    i = pattern.index(after: close)
                    continue
                }
            }
            out.append(pattern[i])
            i = pattern.index(after: i)
        }
        return out
    }

    static func tokenValue(_ token: String, base: String, ext: String, index: Int, item: RenameItem) -> String? {
        let parts = token.split(separator: ":", maxSplits: 1).map(String.init)
        let key = parts.first?.lowercased() ?? ""
        let arg = parts.count > 1 ? parts[1] : nil
        switch key {
        case "name": return base
        case "ext": return ext
        case "parent": return item.url.deletingLastPathComponent().lastPathComponent
        case "n":
            let width = Int(arg ?? "") ?? 1
            let s = String(abs(index))
            return (index < 0 ? "-" : "") + String(repeating: "0", count: max(0, width - s.count)) + s
        case "date", "created":
            guard let d = key == "date" ? item.modified : item.created else { return "" }
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = arg ?? "yyyy-MM-dd"
            return f.string(from: d)
        default: return nil
        }
    }

    static func applyCase(_ s: String, _ c: CaseChange) -> String {
        switch c {
        case .none: return s
        case .lower: return s.lowercased()
        case .upper: return s.uppercased()
        case .title: return s.capitalized
        case .sentence:
            let lower = s.lowercased()
            guard let f = lower.first else { return lower }
            return f.uppercased() + lower.dropFirst()
        }
    }
}
