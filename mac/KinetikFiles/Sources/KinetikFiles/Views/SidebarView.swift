import SwiftUI
import AppKit
import KinetikFilesCore

/// Favourites, cloud folders, disks (with how full they are) and Finder tags.
struct SidebarView: View {
    @EnvironmentObject private var window: WindowModel
    @ObservedObject private var settings = AppSettings.shared
    @Environment(\.look) private var look
    @State private var volumes: [URL] = []
    @State private var clouds: [URL] = []

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 2) {
                section("Favourites")
                ForEach(settings.favourites, id: \.self) { path in
                    let url = URL(fileURLWithPath: path)
                    SidebarRow(url: url, symbol: symbol(for: url), title: FileManager.default.displayName(atPath: path))
                        .contextMenu {
                            Button("Open in New Tab") { window.newTab(url) }
                            Button("Remove from Favourites") { settings.favourites.removeAll { $0 == path } }
                        }
                }
                .onMove { settings.favourites.move(fromOffsets: $0, toOffset: $1) }

                if !clouds.isEmpty {
                    section("Cloud")
                    ForEach(clouds, id: \.self) { u in
                        SidebarRow(url: u, symbol: u.path.contains("Mobile Documents") ? "icloud" : "cloud", title: cloudName(u))
                    }
                }

                section("Locations")
                ForEach(volumes, id: \.self) { v in
                    VolumeRow(url: v) { refreshVolumes() }
                }

                section("Tags")
                ForEach(TagColours.names, id: \.self) { t in
                    Button {
                        window.pane.searchSubfolders = true
                        window.pane.filterText = "tag:\(t)"
                    } label: {
                        HStack(spacing: 8) {
                            Circle().fill(TagColours.colour(t)).frame(width: 10, height: 10).frame(width: 18)
                            Text(t).font(.system(size: 12.5))
                            Spacer()
                        }
                        .padding(.horizontal, 8).frame(height: 26)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(8)
        }
        .background(look.nav)
        .dropDestination(for: URL.self) { urls, _ in
            for u in urls where (try? u.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true {
                if !settings.favourites.contains(u.path) { settings.favourites.append(u.path) }
            }
            return true
        }
        .onAppear { refreshVolumes(); findClouds() }
        .onReceive(NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.didMountNotification)) { _ in refreshVolumes() }
        .onReceive(NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.didUnmountNotification)) { _ in refreshVolumes() }
    }

    private func section(_ title: String) -> some View {
        Text(title.uppercased())
            .font(.system(size: 10, weight: .semibold))
            .foregroundStyle(look.sub)
            .padding(.horizontal, 8)
            .padding(.top, 12).padding(.bottom, 2)
    }

    private func symbol(for url: URL) -> String {
        let home = FileManager.default.homeDirectoryForCurrentUser
        switch url.standardizedFileURL.path {
        case home.path: return "house"
        case home.appendingPathComponent("Desktop").path: return "menubar.dock.rectangle"
        case home.appendingPathComponent("Documents").path: return "doc"
        case home.appendingPathComponent("Downloads").path: return "arrow.down.circle"
        case home.appendingPathComponent("Pictures").path: return "photo"
        case home.appendingPathComponent("Movies").path: return "film"
        case home.appendingPathComponent("Music").path: return "music.note"
        case "/Applications": return "app.dashed"
        default: return "folder"
        }
    }

    private func refreshVolumes() {
        let keys: [URLResourceKey] = [.volumeIsBrowsableKey, .volumeNameKey]
        volumes = (FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) ?? [])
            .filter { (try? $0.resourceValues(forKeys: [.volumeIsBrowsableKey]))?.volumeIsBrowsable ?? true }
    }

    /// iCloud Drive plus Dropbox, Google Drive, OneDrive and friends (which live in ~/Library/CloudStorage).
    private func findClouds() {
        let home = FileManager.default.homeDirectoryForCurrentUser
        var found: [URL] = []
        let icloud = home.appendingPathComponent("Library/Mobile Documents/com~apple~CloudDocs")
        if FileManager.default.fileExists(atPath: icloud.path) { found.append(icloud) }
        let storage = home.appendingPathComponent("Library/CloudStorage")
        if let list = try? FileManager.default.contentsOfDirectory(at: storage, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) {
            found += list.sorted { $0.lastPathComponent < $1.lastPathComponent }
        }
        clouds = found
    }

    private func cloudName(_ u: URL) -> String {
        if u.path.contains("Mobile Documents") { return "iCloud Drive" }
        // "GoogleDrive-me@gmail.com" -> "Google Drive"; "OneDrive-Personal" -> "OneDrive"
        let raw = u.lastPathComponent.split(separator: "-").first.map(String.init) ?? u.lastPathComponent
        switch raw {
        case "GoogleDrive": return "Google Drive"
        case "OneDrive": return "OneDrive"
        case "Dropbox": return "Dropbox"
        case "Box": return "Box"
        default: return raw
        }
    }
}

struct SidebarRow: View {
    let url: URL
    let symbol: String
    let title: String
    @EnvironmentObject private var window: WindowModel
    @Environment(\.look) private var look
    @State private var hover = false

    private var isCurrent: Bool { window.pane.location.standardizedFileURL == url.standardizedFileURL }

    var body: some View {
        Button { window.go(to: url) } label: {
            HStack(spacing: 8) {
                Image(systemName: symbol)
                    .font(.system(size: 13))
                    .foregroundStyle(isCurrent ? look.accent : look.sub)
                    .frame(width: 18)
                Text(title).font(.system(size: 12.5)).lineLimit(1)
                Spacer()
            }
            .padding(.horizontal, 8)
            .frame(height: 26)
            .background(RoundedRectangle(cornerRadius: 6).fill(isCurrent ? look.accent.opacity(0.16) : hover ? look.hover : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .dropDestination(for: URL.self) { urls, _ in
            Drops.receive(urls, into: url, pane: window.pane)
            return true
        }
    }
}

/// A disk with a fill bar and an eject button.
private struct VolumeRow: View {
    let url: URL
    let ejected: () -> Void
    @EnvironmentObject private var window: WindowModel
    @Environment(\.look) private var look

    var body: some View {
        let info = Self.info(url)
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 8) {
                Image(systemName: info.internal ? "internaldrive" : info.network ? "network" : "externaldrive")
                    .font(.system(size: 13)).foregroundStyle(look.sub).frame(width: 18)
                Text(info.name).font(.system(size: 12.5)).lineLimit(1)
                Spacer()
                if info.ejectable {
                    Button {
                        try? NSWorkspace.shared.unmountAndEjectDevice(at: url)
                        ejected()
                    } label: { Image(systemName: "eject.fill").font(.system(size: 10)).foregroundStyle(look.sub) }
                    .buttonStyle(.plain)
                    .help("Eject")
                }
            }
            if info.total > 0 {
                GeometryReader { g in
                    ZStack(alignment: .leading) {
                        Capsule().fill(look.border)
                        Capsule().fill(info.fraction > 0.9 ? look.warn : look.accent)
                            .frame(width: g.size.width * info.fraction)
                    }
                }
                .frame(height: 3)
                .padding(.leading, 26)
                Text("\(PathTools.size(info.free)) free of \(PathTools.size(info.total))")
                    .font(.system(size: 10)).foregroundStyle(look.sub).padding(.leading, 26)
            }
        }
        .padding(.horizontal, 8).padding(.vertical, 4)
        .contentShape(Rectangle())
        .onTapGesture { window.go(to: url) }
    }

    struct Info { var name: String; var total: Int64; var free: Int64; var `internal`: Bool; var network: Bool; var ejectable: Bool
        var fraction: Double { total > 0 ? min(1, Double(total - free) / Double(total)) : 0 }
    }

    static func info(_ url: URL) -> Info {
        let v = try? url.resourceValues(forKeys: [.volumeNameKey, .volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey,
                                                  .volumeIsInternalKey, .volumeIsLocalKey, .volumeIsEjectableKey, .volumeIsRemovableKey])
        return Info(
            name: v?.volumeName ?? url.lastPathComponent,
            total: Int64(v?.volumeTotalCapacity ?? 0),
            free: v?.volumeAvailableCapacityForImportantUsage ?? 0,
            internal: v?.volumeIsInternal ?? false,
            network: !(v?.volumeIsLocal ?? true),
            ejectable: (v?.volumeIsEjectable ?? false) || (v?.volumeIsRemovable ?? false) || !(v?.volumeIsLocal ?? true)
        )
    }
}
