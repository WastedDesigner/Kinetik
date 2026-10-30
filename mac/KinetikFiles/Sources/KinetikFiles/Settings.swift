import SwiftUI
import AppKit

enum Defaults {
    /// Shared by every Kinetik app on the Mac. Theme and Accent are unprefixed (shared); Kinetik Files keys start with "files.".
    static let suite: UserDefaults = UserDefaults(suiteName: "com.wasteddesigner.kinetik") ?? .standard

    /// ~/Library/Application Support/Kinetik/Files, or a folder beside the app in portable mode.
    static var dataFolder: URL {
        let bundleFolder = Bundle.main.bundleURL.deletingLastPathComponent()
        let folder: URL
        if FileManager.default.fileExists(atPath: bundleFolder.appendingPathComponent("KinetikFiles.portable").path) {
            folder = bundleFolder.appendingPathComponent("Kinetik Files Data")
        } else {
            folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
                .appendingPathComponent("Kinetik/Files")
        }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder
    }
}

enum RowDensity: String, CaseIterable, Identifiable {
    case compact = "Compact", normal = "Normal", roomy = "Roomy"
    var id: String { rawValue }
    var height: CGFloat { switch self { case .compact: 22; case .normal: 26; case .roomy: 32 } }
}

enum TerminalApp: String, CaseIterable, Identifiable {
    case terminal = "Terminal", iterm = "iTerm", warp = "Warp", ghostty = "Ghostty"
    var id: String { rawValue }
    var bundleID: String {
        switch self {
        case .terminal: "com.apple.Terminal"
        case .iterm: "com.googlecode.iterm2"
        case .warp: "dev.warp.Warp-Stable"
        case .ghostty: "com.mitchellh.ghostty"
        }
    }
}

/// Every Kinetik Files setting, stored in the shared suite.
final class AppSettings: ObservableObject {
    static let shared = AppSettings()
    private let d = Defaults.suite

    @Published var showHidden: Bool { didSet { d.set(showHidden, forKey: "files.showHidden") } }
    @Published var showExtensions: Bool { didSet { d.set(showExtensions, forKey: "files.showExtensions") } }
    @Published var foldersFirst: Bool { didSet { d.set(foldersFirst, forKey: "files.foldersFirst") } }
    @Published var density: RowDensity { didSet { d.set(density.rawValue, forKey: "files.density") } }
    @Published var zoom: Double { didSet { d.set(zoom, forKey: "files.zoom") } }
    @Published var calculateFolderSizes: Bool { didSet { d.set(calculateFolderSizes, forKey: "files.folderSizes") } }
    @Published var terminal: TerminalApp { didSet { d.set(terminal.rawValue, forKey: "files.terminal") } }
    @Published var hotkeyEnabled: Bool { didSet { d.set(hotkeyEnabled, forKey: "files.hotkey"); Hotkey.shared.update() } }
    @Published var keepRunning: Bool { didSet { d.set(keepRunning, forKey: "files.keepRunning") } }
    @Published var homeFolder: String { didSet { d.set(homeFolder, forKey: "files.home") } }
    @Published var confirmTrash: Bool { didSet { d.set(confirmTrash, forKey: "files.confirmTrash") } }
    @Published var favourites: [String] { didSet { d.set(favourites, forKey: "files.favourites") } }

    private init() {
        func b(_ k: String, _ def: Bool) -> Bool { d.object(forKey: k) == nil ? def : d.bool(forKey: k) }
        let home = FileManager.default.homeDirectoryForCurrentUser
        showHidden = b("files.showHidden", false)
        showExtensions = b("files.showExtensions", true)
        foldersFirst = b("files.foldersFirst", true)
        density = RowDensity(rawValue: d.string(forKey: "files.density") ?? "") ?? .normal
        zoom = d.object(forKey: "files.zoom") == nil ? 1.0 : d.double(forKey: "files.zoom")
        calculateFolderSizes = b("files.folderSizes", true)
        terminal = TerminalApp(rawValue: d.string(forKey: "files.terminal") ?? "") ?? .terminal
        hotkeyEnabled = b("files.hotkey", true)
        keepRunning = b("files.keepRunning", false)
        homeFolder = d.string(forKey: "files.home") ?? home.path
        confirmTrash = b("files.confirmTrash", false)
        favourites = d.stringArray(forKey: "files.favourites") ?? [
            home.path,
            home.appendingPathComponent("Desktop").path,
            home.appendingPathComponent("Documents").path,
            home.appendingPathComponent("Downloads").path,
            home.appendingPathComponent("Pictures").path,
            "/Applications",
        ]
    }

    var rowHeight: CGFloat { density.height * zoom }
    var iconSize: CGFloat { (density == .compact ? 16 : density == .normal ? 18 : 22) * zoom }
}
