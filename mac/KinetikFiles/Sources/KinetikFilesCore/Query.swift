import Foundation

// The search language, the same as on Windows:
//
//   holiday photos          both words in the name
//   "holiday photos"        the exact phrase
//   cat OR dog, cat | dog   either
//   -draft, NOT draft       not this
//   (a OR b) c              grouping
//   *.psd, img_??.jpg       wildcards
//   /^IMG_\d{4}/            regular expression on the name
//   ext:jpg,png             extension
//   kind:image              folder, image, video, audio, document, archive, code, design, 3d, font, app
//   size:>10mb  size:<500kb  size:1mb..1gb     (units are 1000-based, like Finder)
//   modified:<7d            changed in the last 7 days (m, h, d, w, mo, y)
//   modified:>2026-01-01    changed after that day; also today, yesterday, a..b ranges
//   created:…  added:…      same, for the created / date added date
//   tag:red                 Finder tag
//   content:"some text"     inside the file (the app checks this, see `contentTerms`)
//   name:report             explicit name match (useful for words like OR)

public struct FileFacts: Sendable {
    public var name: String
    public var isFolder: Bool
    public var size: Int64?
    public var modified: Date?
    public var created: Date?
    public var added: Date?
    public var tags: [String]

    public init(name: String, isFolder: Bool, size: Int64? = nil, modified: Date? = nil, created: Date? = nil, added: Date? = nil, tags: [String] = []) {
        self.name = name; self.isFolder = isFolder; self.size = size
        self.modified = modified; self.created = created; self.added = added; self.tags = tags
    }

    public var ext: String {
        guard !isFolder, let dot = name.lastIndex(of: "."), dot != name.startIndex else { return "" }
        return String(name[name.index(after: dot)...]).lowercased()
    }
}

public enum DateField: String, Sendable { case modified, created, added }

public indirect enum QueryNode {
    case all
    case and([QueryNode])
    case or([QueryNode])
    case not(QueryNode)
    case name(String)                 // case-insensitive contains
    case pattern(NSRegularExpression) // wildcard or /regex/
    case ext(Set<String>)
    case kind(String)
    case size(Int64?, Int64?)         // inclusive bounds
    case date(DateField, Date?, Date?)
    case tag(String)
    case content(String)
}

public struct Query {
    public let text: String
    public let root: QueryNode
    public let error: String?

    public var isEmpty: Bool { if case .all = root { return true } else { return false } }

    /// Every content: term, so the app knows whether it has to open files.
    public var contentTerms: [String] { Query.collectContent(root) }

    public static func parse(_ text: String, now: Date = Date(), calendar: Calendar = .current) -> Query {
        var p = Parser(tokens: tokenize(text), now: now, calendar: calendar)
        var parts: [QueryNode] = []
        while p.pos < p.tokens.count {
            if let node = p.parseExpression() { parts.append(node) }
            if p.peek == .close { p.pos += 1; p.error = p.error ?? "There's a closing bracket without an opening one" }
        }
        let root: QueryNode = parts.isEmpty ? .all : parts.count == 1 ? parts[0] : .and(parts)
        return Query(text: text, root: root, error: p.error)
    }

    /// `content` answers content: terms; when nil they count as matching (the caller filters later).
    public func matches(_ f: FileFacts, content: ((String) -> Bool)? = nil) -> Bool {
        Query.eval(root, f, content)
    }

    static func eval(_ n: QueryNode, _ f: FileFacts, _ content: ((String) -> Bool)?) -> Bool {
        switch n {
        case .all: return true
        case .and(let a): return a.allSatisfy { eval($0, f, content) }
        case .or(let a): return a.contains { eval($0, f, content) }
        case .not(let x): return !eval(x, f, content)
        case .name(let s): return f.name.range(of: s, options: [.caseInsensitive, .diacriticInsensitive]) != nil
        case .pattern(let r): return r.firstMatch(in: f.name, range: NSRange(f.name.startIndex..., in: f.name)) != nil
        case .ext(let set): return set.contains(f.ext)
        case .kind(let k): return Kinds.matches(k, f)
        case .size(let lo, let hi):
            guard !f.isFolder || f.size != nil, let s = f.size else { return false }
            return (lo.map { s >= $0 } ?? true) && (hi.map { s <= $0 } ?? true)
        case .date(let field, let lo, let hi):
            let d: Date? = field == .modified ? f.modified : field == .created ? f.created : f.added
            guard let d else { return false }
            return (lo.map { d >= $0 } ?? true) && (hi.map { d <= $0 } ?? true)
        case .tag(let t): return f.tags.contains { $0.caseInsensitiveCompare(t) == .orderedSame }
        case .content(let s): return content?(s) ?? true
        }
    }

    static func collectContent(_ n: QueryNode) -> [String] {
        switch n {
        case .content(let s): return [s]
        case .and(let a), .or(let a): return a.flatMap(collectContent)
        case .not(let x): return collectContent(x)
        default: return []
        }
    }

    // MARK: - Tokens

    enum Token: Equatable { case open, close, or, not, word(String, quoted: Bool), regex(String) }

    static func tokenize(_ s: String) -> [Token] {
        var tokens: [Token] = []
        let chars = Array(s)
        var i = 0
        while i < chars.count {
            let c = chars[i]
            if c.isWhitespace { i += 1; continue }
            if c == "(" { tokens.append(.open); i += 1; continue }
            if c == ")" { tokens.append(.close); i += 1; continue }
            if c == "|" { tokens.append(.or); i += 1; continue }
            if c == "-", i + 1 < chars.count, !chars[i + 1].isWhitespace { tokens.append(.not); i += 1; continue }
            if c == "/" {
                var j = i + 1, body = ""
                while j < chars.count && chars[j] != "/" {
                    if chars[j] == "\\", j + 1 < chars.count { body.append(chars[j]); body.append(chars[j + 1]); j += 2; continue }
                    body.append(chars[j]); j += 1
                }
                if j < chars.count { tokens.append(.regex(body)); i = j + 1; continue }
                // No closing slash: treat as an ordinary word.
            }
            if c == "\"" {
                var j = i + 1, body = ""
                while j < chars.count && chars[j] != "\"" { body.append(chars[j]); j += 1 }
                tokens.append(.word(body, quoted: true))
                i = min(j + 1, chars.count)
                continue
            }
            var word = ""
            while i < chars.count && !chars[i].isWhitespace && chars[i] != "(" && chars[i] != ")" {
                if chars[i] == "\"" {
                    var j = i + 1
                    while j < chars.count && chars[j] != "\"" { word.append(chars[j]); j += 1 }
                    i = min(j + 1, chars.count)
                    continue
                }
                word.append(chars[i]); i += 1
            }
            switch word {
            case "OR": tokens.append(.or)
            case "NOT": tokens.append(.not)
            case "AND": break
            default: tokens.append(.word(word, quoted: false))
            }
        }
        return tokens
    }

    // MARK: - Parser

    struct Parser {
        let tokens: [Token]
        var pos = 0
        let now: Date
        let calendar: Calendar
        var error: String?

        init(tokens: [Token], now: Date, calendar: Calendar) {
            self.tokens = tokens; self.now = now; self.calendar = calendar
        }

        var peek: Token? { pos < tokens.count ? tokens[pos] : nil }

        mutating func parseExpression() -> QueryNode? {
            var parts: [QueryNode] = []
            if let first = parseAnd() { parts.append(first) }
            while peek == .or {
                pos += 1
                if let next = parseAnd() { parts.append(next) }
            }
            if parts.isEmpty { return nil }
            return parts.count == 1 ? parts[0] : .or(parts)
        }

        mutating func parseAnd() -> QueryNode? {
            var parts: [QueryNode] = []
            while let t = peek, t != .or, t != .close {
                if let u = parseUnary() { parts.append(u) }
            }
            if parts.isEmpty { return nil }
            return parts.count == 1 ? parts[0] : .and(parts)
        }

        mutating func parseUnary() -> QueryNode? {
            guard let t = peek else { return nil }
            pos += 1
            switch t {
            case .not:
                guard let inner = parseUnary() else { return nil }
                return .not(inner)
            case .open:
                let inner = parseExpression()
                if peek == .close { pos += 1 } else { error = error ?? "A bracket isn't closed" }
                return inner
            case .close, .or:
                return nil
            case .regex(let body):
                do { return .pattern(try NSRegularExpression(pattern: body, options: [.caseInsensitive])) }
                catch { self.error = self.error ?? "The regular expression isn't valid"; return nil }
            case .word(let w, let quoted):
                return term(w, quoted: quoted)
            }
        }

        mutating func term(_ w: String, quoted: Bool) -> QueryNode? {
            if w.isEmpty { return nil }
            if quoted { return .name(w) }
            if let colon = w.firstIndex(of: ":"), colon != w.startIndex {
                let key = w[..<colon].lowercased()
                let value = String(w[w.index(after: colon)...])
                switch key {
                case "name": return value.isEmpty ? nil : wordOrPattern(value)
                case "ext", "type":
                    let set = Set(value.lowercased().split(separator: ",").map { $0.hasPrefix(".") ? String($0.dropFirst()) : String($0) })
                    return set.isEmpty ? nil : .ext(set)
                case "kind": return value.isEmpty ? nil : .kind(value.lowercased())
                case "tag": return value.isEmpty ? nil : .tag(value)
                case "content", "text": return value.isEmpty ? nil : .content(value)
                case "size":
                    if let r = sizeRange(value) { return .size(r.0, r.1) }
                    error = error ?? "Size should look like size:>10mb or size:1mb..5mb"; return nil
                case "modified", "date", "created", "added":
                    let field: DateField = key == "created" ? .created : key == "added" ? .added : .modified
                    if let r = dateRange(value) { return .date(field, r.0, r.1) }
                    error = error ?? "Dates should look like \(key):<7d or \(key):>2026-01-01"; return nil
                default: break // Not an operator (e.g. a time "10:30"), search for it as text.
                }
            }
            return wordOrPattern(w)
        }

        func wordOrPattern(_ w: String) -> QueryNode {
            guard w.contains("*") || w.contains("?") else { return .name(w) }
            var p = "^"
            for ch in w {
                switch ch {
                case "*": p += ".*"
                case "?": p += "."
                default: p += NSRegularExpression.escapedPattern(for: String(ch))
                }
            }
            p += "$"
            return (try? NSRegularExpression(pattern: p, options: [.caseInsensitive])).map { .pattern($0) } ?? .name(w)
        }

        // MARK: Sizes

        static func bytes(_ s: String) -> Int64? {
            let t = s.lowercased().trimmingCharacters(in: .whitespaces)
            let units: [(String, Double)] = [("tb", 1e12), ("gb", 1e9), ("mb", 1e6), ("kb", 1e3), ("t", 1e12), ("g", 1e9), ("m", 1e6), ("k", 1e3), ("b", 1)]
            for (u, mult) in units where t.hasSuffix(u) {
                guard let n = Double(t.dropLast(u.count)) else { return nil }
                return Int64(n * mult)
            }
            return Double(t).map { Int64($0) }
        }

        func sizeRange(_ v: String) -> (Int64?, Int64?)? {
            if let r = v.range(of: "..") {
                guard let a = Parser.bytes(String(v[..<r.lowerBound])), let b = Parser.bytes(String(v[r.upperBound...])) else { return nil }
                return (a, b)
            }
            let (op, rest) = Parser.comparison(v)
            guard let n = Parser.bytes(rest) else { return nil }
            switch op {
            case ">": return (n + 1, nil)
            case ">=": return (n, nil)
            case "<": return (nil, n - 1)
            case "<=": return (nil, n)
            default: return (n, n)
            }
        }

        static func comparison(_ v: String) -> (String, String) {
            for op in [">=", "<=", ">", "<", "="] where v.hasPrefix(op) { return (op, String(v.dropFirst(op.count))) }
            return ("=", v)
        }

        // MARK: Dates

        /// An age like 7d or 3mo, in seconds.
        static func age(_ s: String) -> TimeInterval? {
            let t = s.lowercased()
            let units: [(String, Double)] = [("mo", 2_629_800), ("min", 60), ("m", 60), ("h", 3600), ("d", 86400), ("w", 604_800), ("y", 31_557_600)]
            for (u, secs) in units where t.hasSuffix(u) {
                guard let n = Double(t.dropLast(u.count)), n >= 0 else { return nil }
                return n * secs
            }
            return nil
        }

        /// A whole day (start, end) for 2026-01-31, 2026-01, 2026, today or yesterday.
        func day(_ s: String) -> (Date, Date)? {
            let t = s.lowercased()
            let startToday = calendar.startOfDay(for: now)
            func span(_ start: Date, _ comp: Calendar.Component) -> (Date, Date)? {
                guard let end = calendar.date(byAdding: comp, value: 1, to: start) else { return nil }
                return (start, end.addingTimeInterval(-0.001))
            }
            if t == "today" { return span(startToday, .day) }
            if t == "yesterday", let y = calendar.date(byAdding: .day, value: -1, to: startToday) { return span(y, .day) }
            let parts = t.split(separator: "-").map { Int($0) }
            guard !parts.isEmpty, parts.allSatisfy({ $0 != nil }) else { return nil }
            var c = DateComponents()
            c.year = parts[0]; c.month = parts.count > 1 ? parts[1] : 1; c.day = parts.count > 2 ? parts[2] : 1
            guard let y = c.year, y > 1900, y < 3000, let start = calendar.date(from: c) else { return nil }
            return span(start, parts.count == 1 ? .year : parts.count == 2 ? .month : .day)
        }

        func dateRange(_ v: String) -> (Date?, Date?)? {
            if let r = v.range(of: "..") {
                guard let a = day(String(v[..<r.lowerBound])), let b = day(String(v[r.upperBound...])) else { return nil }
                return (a.0, b.1)
            }
            let (op, rest) = Parser.comparison(v)
            if let secs = Parser.age(rest) {
                let edge = now.addingTimeInterval(-secs)
                // "<7d" means younger than 7 days, so modified after the edge.
                switch op {
                case ">", ">=": return (nil, edge)
                default: return (edge, nil)
                }
            }
            guard let d = day(rest) else { return nil }
            switch op {
            case ">": return (d.1.addingTimeInterval(0.001), nil)
            case ">=": return (d.0, nil)
            case "<": return (nil, d.0.addingTimeInterval(-0.001))
            case "<=": return (nil, d.1)
            default: return (d.0, d.1)
            }
        }
    }
}

// File kinds by extension, shared with the app for icons and filters.
public enum Kinds {
    public static let map: [String: Set<String>] = [
        "image": ["jpg", "jpeg", "png", "gif", "heic", "heif", "webp", "tif", "tiff", "bmp", "svg", "raw", "cr2", "cr3", "nef", "arw", "dng", "raf", "orf", "avif", "ico", "icns"],
        "video": ["mp4", "mov", "m4v", "mkv", "avi", "webm", "wmv", "flv", "mpg", "mpeg"],
        "audio": ["mp3", "m4a", "aac", "wav", "aiff", "aif", "flac", "ogg", "opus", "alac"],
        "document": ["pdf", "doc", "docx", "pages", "txt", "md", "rtf", "odt", "xls", "xlsx", "numbers", "csv", "ppt", "pptx", "key", "epub"],
        "archive": ["zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "xz", "dmg", "iso", "pkg"],
        "code": ["swift", "cs", "js", "ts", "tsx", "jsx", "py", "rb", "go", "rs", "c", "h", "cpp", "hpp", "m", "java", "kt", "json", "yaml", "yml", "xml", "html", "css", "sh", "lua", "gd", "shader", "hlsl", "glsl"],
        "design": ["psd", "ai", "fig", "sketch", "xd", "afdesign", "afphoto", "indd", "blend", "kra", "procreate", "aseprite"],
        "3d": ["fbx", "obj", "glb", "gltf", "usdz", "usd", "stl", "dae", "3ds", "blend"],
        "font": ["ttf", "otf", "woff", "woff2", "ttc"],
        "app": ["app"],
    ]

    public static func kind(of f: FileFacts) -> String {
        if f.isFolder { return f.ext == "app" || f.name.lowercased().hasSuffix(".app") ? "app" : "folder" }
        for (k, set) in map where set.contains(f.ext) { return k }
        return "other"
    }

    static func matches(_ k: String, _ f: FileFacts) -> Bool {
        let want = k == "picture" || k == "photo" ? "image" : k == "music" ? "audio" : k == "movie" ? "video" : k == "doc" ? "document" : k
        if want == "folder" { return f.isFolder && !f.name.lowercased().hasSuffix(".app") }
        if want == "app" { return f.name.lowercased().hasSuffix(".app") }
        if want == "file" { return !f.isFolder }
        return map[want]?.contains(f.ext) ?? false
    }
}
