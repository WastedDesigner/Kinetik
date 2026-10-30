import SwiftUI
import AppKit
import QuickLookThumbnailing
import KinetikFilesCore

/// The file's own icon from macOS.
struct FileIcon: View {
    let url: URL
    var size: CGFloat = 18

    var body: some View {
        Image(nsImage: Icons.shared.icon(for: url))
            .resizable()
            .interpolation(.high)
            .frame(width: size, height: size)
    }
}

/// A Quick Look thumbnail (the picture itself for images and videos), falling back to the icon.
struct Thumbnail: View {
    let url: URL
    let size: CGFloat
    @State private var image: NSImage?

    var body: some View {
        Group {
            if let image {
                Image(nsImage: image).resizable().aspectRatio(contentMode: .fit)
            } else {
                FileIcon(url: url, size: size * 0.8)
            }
        }
        .frame(width: size, height: size)
        .task(id: url) { image = await ThumbnailCache.shared.thumbnail(for: url, size: size) }
    }
}

actor ThumbnailCache {
    static let shared = ThumbnailCache()
    private var cache: [String: NSImage] = [:]
    private var order: [String] = []

    func thumbnail(for url: URL, size: CGFloat) async -> NSImage? {
        let key = "\(url.path)#\(Int(size))"
        if let i = cache[key] { return i }
        let scale = await MainActor.run { NSScreen.main?.backingScaleFactor ?? 2 }
        let request = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: size, height: size), scale: scale, representationTypes: .thumbnail)
        guard let rep = try? await QLThumbnailGenerator.shared.generateBestRepresentation(for: request) else { return nil }
        let image = rep.nsImage
        cache[key] = image
        order.append(key)
        if order.count > 600 { cache[order.removeFirst()] = nil }
        return image
    }
}

/// Finder's tag colours. Custom tags get grey.
enum TagColours {
    static let names = ["Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Gray"]
    static func colour(_ tag: String) -> Color {
        switch tag.lowercased() {
        case "red": Color(red: 1.0, green: 0.27, blue: 0.27)
        case "orange": Color(red: 1.0, green: 0.6, blue: 0.2)
        case "yellow": Color(red: 1.0, green: 0.82, blue: 0.2)
        case "green": Color(red: 0.3, green: 0.8, blue: 0.4)
        case "blue": Color(red: 0.25, green: 0.55, blue: 1.0)
        case "purple": Color(red: 0.7, green: 0.4, blue: 0.95)
        default: Color.gray
        }
    }
}

struct TagDots: View {
    let tags: [String]
    var body: some View {
        HStack(spacing: -3) {
            ForEach(tags.prefix(4), id: \.self) { t in
                Circle().fill(TagColours.colour(t)).frame(width: 9, height: 9)
                    .overlay(Circle().stroke(Color.black.opacity(0.25), lineWidth: 0.5))
            }
        }
    }
}

/// A small borderless icon button in the Kinetik style.
struct IconButton: View {
    let symbol: String
    var help: String = ""
    var enabled = true
    let action: () -> Void
    @Environment(\.look) private var look
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(enabled ? look.text : look.sub.opacity(0.5))
                .frame(width: 26, height: 24)
                .background(RoundedRectangle(cornerRadius: 6).fill(hover && enabled ? look.hover : .clear))
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .help(help)
        .onHover { hover = $0 }
    }
}

/// A text field drawn in the Kinetik style.
struct KField: View {
    let placeholder: String
    @Binding var text: String
    var symbol: String? = nil
    @Environment(\.look) private var look

    var body: some View {
        HStack(spacing: 6) {
            if let symbol { Image(systemName: symbol).foregroundStyle(look.sub).font(.system(size: 12)) }
            TextField(placeholder, text: $text).textFieldStyle(.plain)
        }
        .padding(.horizontal, 8).padding(.vertical, 5)
        .background(RoundedRectangle(cornerRadius: 6).fill(look.field))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(look.border, lineWidth: 1))
    }
}

enum Format {
    static let date: DateFormatter = {
        let f = DateFormatter()
        f.dateStyle = .medium
        f.timeStyle = .short
        f.doesRelativeDateFormatting = true
        return f
    }()

    static func date(_ d: Date?) -> String { d.map { date.string(from: $0) } ?? "—" }

    static func size(_ item: FileItem, folderSize: Int64?) -> String {
        if item.isFolder { return folderSize.map(PathTools.size) ?? "—" }
        return PathTools.size(item.size ?? 0)
    }
}

/// Opens the right-click menu's "Open With" list.
struct OpenWithMenu: View {
    let urls: [URL]
    var body: some View {
        Menu("Open With") {
            if let first = urls.first {
                let apps = NSWorkspace.shared.urlsForApplications(toOpen: first)
                ForEach(apps.prefix(20), id: \.self) { app in
                    Button(FileManager.default.displayName(atPath: app.path)) {
                        NSWorkspace.shared.open(urls, withApplicationAt: app, configuration: NSWorkspace.OpenConfiguration())
                    }
                }
            }
        }
    }
}
