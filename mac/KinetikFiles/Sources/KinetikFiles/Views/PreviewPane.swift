import SwiftUI
import AppKit
import Quartz
import CryptoKit
import KinetikFilesCore

/// The right-hand pane: a Quick Look preview of the selection, its details, and file hashes.
struct PreviewPane: View {
    @ObservedObject var pane: PaneModel
    @Environment(\.look) private var look

    var body: some View {
        let sel = pane.selectedItems
        VStack(alignment: .leading, spacing: 0) {
            if sel.count == 1 {
                SinglePreview(item: sel[0], folderSize: pane.folderSizes[sel[0].url])
                    .id(sel[0].url)
            } else if sel.count > 1 {
                MultiSummary(items: sel, folderSizes: pane.folderSizes)
            } else {
                FolderSummary(pane: pane)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(look.nav)
    }
}

private struct SinglePreview: View {
    let item: FileItem
    let folderSize: Int64?
    @Environment(\.look) private var look
    @State private var hashes: [String: String] = [:]
    @State private var hashing = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Group {
                    if item.isFolder {
                        FileIcon(url: item.url, size: 128).frame(maxWidth: .infinity)
                    } else {
                        QuickLookView(url: item.url)
                            .frame(height: 240)
                            .clipShape(RoundedRectangle(cornerRadius: 8))
                    }
                }
                .padding(.top, 12)

                Text(item.name).font(.system(size: 14, weight: .semibold)).textSelection(.enabled)
                Text(item.kindDescription).foregroundStyle(look.sub).font(.system(size: 12))

                VStack(alignment: .leading, spacing: 6) {
                    row("Size", Format.size(item, folderSize: folderSize))
                    row("Modified", Format.date(item.modified))
                    row("Created", Format.date(item.created))
                    if item.added != nil { row("Added", Format.date(item.added)) }
                    row("Where", item.url.deletingLastPathComponent().path)
                    if !item.tags.isEmpty {
                        HStack(alignment: .top) {
                            Text("Tags").foregroundStyle(look.sub).frame(width: 70, alignment: .leading)
                            VStack(alignment: .leading, spacing: 3) {
                                ForEach(item.tags, id: \.self) { t in
                                    HStack(spacing: 5) { Circle().fill(TagColours.colour(t)).frame(width: 8, height: 8); Text(t) }
                                }
                            }
                        }
                    }
                }
                .font(.system(size: 12))

                if !item.isFolder && !item.isPackage {
                    Divider()
                    HStack {
                        Text("Hashes").font(.system(size: 12, weight: .semibold))
                        Spacer()
                        if hashing { ProgressView().controlSize(.small) }
                        else if hashes.isEmpty { Button("Calculate") { calculate() }.controlSize(.small) }
                    }
                    ForEach(["MD5", "SHA-1", "SHA-256"], id: \.self) { k in
                        if let h = hashes[k] {
                            VStack(alignment: .leading, spacing: 2) {
                                HStack {
                                    Text(k).foregroundStyle(look.sub)
                                    Spacer()
                                    Button("Copy") {
                                        NSPasteboard.general.clearContents()
                                        NSPasteboard.general.setString(h, forType: .string)
                                    }
                                    .buttonStyle(.plain).foregroundStyle(look.accent)
                                }
                                Text(h).font(.system(size: 10.5, design: .monospaced)).textSelection(.enabled).lineLimit(3)
                            }
                            .font(.system(size: 11))
                        }
                    }
                }
            }
            .padding(.horizontal, 14).padding(.bottom, 14)
        }
    }

    private func row(_ label: String, _ value: String) -> some View {
        HStack(alignment: .top) {
            Text(label).foregroundStyle(look.sub).frame(width: 70, alignment: .leading)
            Text(value).textSelection(.enabled).lineLimit(3)
        }
    }

    private func calculate() {
        hashing = true
        let url = item.url
        Task {
            let result = await Task.detached(priority: .userInitiated) { Hashes.all(url) }.value
            hashes = result
            hashing = false
        }
    }
}

enum Hashes {
    /// MD5, SHA-1 and SHA-256 in one read of the file, a megabyte at a time.
    static func all(_ url: URL) -> [String: String] {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return [:] }
        defer { try? handle.close() }
        var md5 = Insecure.MD5(), sha1 = Insecure.SHA1(), sha256 = SHA256()
        while let chunk = try? handle.read(upToCount: 1 << 20), !chunk.isEmpty {
            md5.update(data: chunk); sha1.update(data: chunk); sha256.update(data: chunk)
        }
        func hex<D: Digest>(_ d: D) -> String { d.map { String(format: "%02x", $0) }.joined() }
        return ["MD5": hex(md5.finalize()), "SHA-1": hex(sha1.finalize()), "SHA-256": hex(sha256.finalize())]
    }
}

private struct MultiSummary: View {
    let items: [FileItem]
    let folderSizes: [URL: Int64]
    @Environment(\.look) private var look

    var body: some View {
        VStack(spacing: 10) {
            ZStack {
                ForEach(Array(items.prefix(3).enumerated()), id: \.offset) { i, item in
                    FileIcon(url: item.url, size: 96).rotationEffect(.degrees(Double(i - 1) * 8)).offset(x: CGFloat(i - 1) * 14)
                }
            }
            .frame(height: 140)
            .padding(.top, 20)
            Text("\(items.count) items").font(.system(size: 14, weight: .semibold))
            let bytes = items.reduce(Int64(0)) { $0 + ($1.isFolder ? (folderSizes[$1.url] ?? 0) : ($1.size ?? 0)) }
            Text(PathTools.size(bytes)).foregroundStyle(look.sub)
            let folders = items.filter(\.isFolder).count
            Text("\(items.count - folders) files, \(folders) folders").foregroundStyle(look.sub).font(.system(size: 12))
        }
        .frame(maxWidth: .infinity)
    }
}

private struct FolderSummary: View {
    @ObservedObject var pane: PaneModel
    @Environment(\.look) private var look

    var body: some View {
        VStack(spacing: 10) {
            FileIcon(url: pane.location, size: 96).padding(.top, 30)
            Text(pane.title).font(.system(size: 14, weight: .semibold))
            Text(pane.items.count == 1 ? "1 item" : "\(pane.items.count) items").foregroundStyle(look.sub)
            Text("Select something to preview it").foregroundStyle(look.sub).font(.system(size: 11)).padding(.top, 8)
        }
        .frame(maxWidth: .infinity)
    }
}

/// Quick Look's own preview view: PDFs, Office and iWork files, images, video, audio, fonts, 3D models, code…
struct QuickLookView: NSViewRepresentable {
    let url: URL

    func makeNSView(context: Context) -> QLPreviewView {
        let v = QLPreviewView(frame: .zero, style: .compact)!
        v.autostarts = true
        v.shouldCloseWithWindow = false
        v.previewItem = url as NSURL
        return v
    }

    func updateNSView(_ v: QLPreviewView, context: Context) {
        if (v.previewItem as? NSURL) as URL? != url { v.previewItem = url as NSURL }
    }

    static func dismantleNSView(_ v: QLPreviewView, coordinator: ()) {
        v.close()
    }
}
