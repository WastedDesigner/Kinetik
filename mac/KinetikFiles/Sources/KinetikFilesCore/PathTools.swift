import Foundation

public enum PathTools {
    /// Turns what someone typed in the address bar into a folder URL: ~, ~/x, relative to `base`, file:// URLs, quotes.
    public static func resolve(_ typed: String, relativeTo base: URL?, home: URL = FileManager.default.homeDirectoryForCurrentUser) -> URL? {
        var t = typed.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.count >= 2, (t.hasPrefix("\"") && t.hasSuffix("\"")) || (t.hasPrefix("'") && t.hasSuffix("'")) {
            t = String(t.dropFirst().dropLast())
        }
        if t.isEmpty { return nil }
        if t.hasPrefix("file://"), let u = URL(string: t) { return u.standardizedFileURL }
        if t == "~" { return home }
        if t.hasPrefix("~/") { return home.appendingPathComponent(String(t.dropFirst(2))).standardizedFileURL }
        if t.hasPrefix("/") { return URL(fileURLWithPath: t).standardizedFileURL }
        guard let base else { return nil }
        return base.appendingPathComponent(t).standardizedFileURL
    }

    /// The folders from the root down to `url`, for the breadcrumb bar.
    public static func crumbs(_ url: URL) -> [URL] {
        var out: [URL] = []
        var u = url.standardizedFileURL
        while true {
            out.append(u)
            let parent = u.deletingLastPathComponent().standardizedFileURL
            if parent.path == u.path || u.path == "/" { break }
            u = parent
        }
        return out.reversed()
    }

    /// A free name in `folder`, Finder style: "Report.pdf", "Report 2.pdf", "Report 3.pdf"…
    /// For copies into the same folder Finder says "Report copy.pdf"; pass `copySuffix: true` for that.
    public static func uniqueName(_ name: String, in folder: URL, copySuffix: Bool = false, exists: (URL) -> Bool) -> String {
        if !exists(folder.appendingPathComponent(name)) && !copySuffix { return name }
        let isPackage = name.hasSuffix(".app")
        var base = name, ext = ""
        if let dot = name.lastIndex(of: "."), dot != name.startIndex, !name[dot...].contains(" ") || isPackage {
            base = String(name[..<dot]); ext = String(name[dot...])
        }
        if copySuffix {
            let first = base + " copy" + ext
            if !exists(folder.appendingPathComponent(first)) { return first }
            var n = 2
            while exists(folder.appendingPathComponent("\(base) copy \(n)\(ext)")) { n += 1 }
            return "\(base) copy \(n)\(ext)"
        }
        // Continue an existing number: "Report 2" -> "Report 3".
        var stem = base, n = 2
        if let space = base.lastIndex(of: " "), let num = Int(base[base.index(after: space)...]), num >= 1 {
            stem = String(base[..<space]); n = num + 1
        }
        while exists(folder.appendingPathComponent("\(stem) \(n)\(ext)")) { n += 1 }
        return "\(stem) \(n)\(ext)"
    }

    /// 1000-based sizes like Finder: "0 bytes", "912 bytes", "4.2 KB", "1.3 GB".
    public static func size(_ bytes: Int64) -> String {
        if bytes < 1000 { return bytes == 1 ? "1 byte" : "\(bytes) bytes" }
        let units = ["KB", "MB", "GB", "TB", "PB"]
        var v = Double(bytes) / 1000, i = 0
        while v >= 1000 && i < units.count - 1 { v /= 1000; i += 1 }
        let s = v >= 100 ? String(format: "%.0f", v) : String(format: "%.1f", v)
        return s + " " + units[i]
    }

    /// Is `child` the same as, or inside, `folder`? Used to refuse copying a folder into itself.
    public static func isInside(_ child: URL, _ folder: URL) -> Bool {
        let c = child.standardizedFileURL.resolvingSymlinksInPath().path
        let f = folder.standardizedFileURL.resolvingSymlinksInPath().path
        return c == f || c.hasPrefix(f.hasSuffix("/") ? f : f + "/")
    }
}
