import AppKit
import Carbon.HIToolbox
import Quartz
import SwiftUI
import UniformTypeIdentifiers

// How Kinetik Files fits into macOS: opening folders from other apps, the global hotkey, the terminal, Finder's Get Info,
// Quick Look, and the "default folder viewer" setting.

/// Routes folders opened from outside (the Dock, `open`, other apps, the hotkey) to a window.
@MainActor
final class AppState: ObservableObject {
    static let shared = AppState()

    /// Every open window, newest last. Weak so closed windows drop out.
    private var windows: [WeakWindow] = []
    private var pending: [URL] = []
    var openWindow: ((URL) -> Void)?

    struct WeakWindow { weak var model: WindowModel? }

    func register(_ w: WindowModel) {
        windows.removeAll { $0.model == nil || $0.model === w }
        windows.append(WeakWindow(model: w))
    }

    func activated(_ w: WindowModel) { register(w) }

    private var keyObservers: [NSObjectProtocol] = []

    /// Moves a window's model to the front of the list whenever that window becomes key.
    func watchKey(_ win: NSWindow, _ model: WindowModel) {
        let o = NotificationCenter.default.addObserver(forName: NSWindow.didBecomeKeyNotification, object: win, queue: .main) { [weak model] _ in
            Task { @MainActor in if let model { AppState.shared.activated(model) } }
        }
        keyObservers.append(o)
    }

    var frontWindow: WindowModel? { windows.last(where: { $0.model != nil })?.model }

    /// The first folder a new window should show.
    func takePending() -> URL? { pending.isEmpty ? nil : pending.removeFirst() }

    /// Opens a folder (or reveals a file) in a new tab of the front window, or a new window if there isn't one.
    func open(_ url: URL) {
        NSApp.activate(ignoringOtherApps: true)
        if let w = frontWindow {
            w.newTab(url)
            NSApp.windows.first { $0.isVisible && $0.canBecomeKey }?.makeKeyAndOrderFront(nil)
        } else {
            pending.append(url)
            openWindow?(url)
        }
    }

    func showOrCreateWindow() {
        NSApp.activate(ignoringOtherApps: true)
        if let win = NSApp.windows.first(where: { $0.isVisible && $0.canBecomeKey }) {
            win.makeKeyAndOrderFront(nil)
        } else {
            open(URL(fileURLWithPath: AppSettings.shared.homeFolder))
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        Hotkey.shared.update()
        NSApp.servicesProvider = ServiceProvider()
    }

    // Folders dropped on the Dock icon, `open -a "Kinetik Files" ~/x`, or other apps asking us to open a folder.
    func application(_ application: NSApplication, open urls: [URL]) {
        Task { @MainActor in for u in urls { AppState.shared.open(u) } }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { Task { @MainActor in AppState.shared.showOrCreateWindow() } }
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        !AppSettings.shared.keepRunning
    }
}

/// "Open in Kinetik Files" in every app's Services menu (declared in Info.plist).
final class ServiceProvider: NSObject {
    @objc func openFolder(_ pboard: NSPasteboard, userData: String, error: AutoreleasingUnsafeMutablePointer<NSString>) {
        let urls = (pboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
        Task { @MainActor in for u in urls { AppState.shared.open(u) } }
    }
}

enum Integration {
    static func openTerminal(at folder: URL, app: TerminalApp) {
        let ws = NSWorkspace.shared
        guard let appURL = ws.urlForApplication(withBundleIdentifier: app.bundleID) ?? ws.urlForApplication(withBundleIdentifier: TerminalApp.terminal.bundleID) else { return }
        let config = NSWorkspace.OpenConfiguration()
        ws.open([folder], withApplicationAt: appURL, configuration: config)
    }

    /// Finder's own Get Info window, so every field (sharing, permissions, Open With) is there.
    static func showFinderInfo(_ urls: [URL]) {
        let list = urls.map { "POSIX file \"\($0.path.replacingOccurrences(of: "\"", with: "\\\""))\"" }.joined(separator: ", ")
        let script = """
        tell application "Finder"
            activate
            repeat with f in {\(list)}
                open information window of (f as alias)
            end repeat
        end tell
        """
        var error: NSDictionary?
        NSAppleScript(source: script)?.executeAndReturnError(&error)
    }

    // MARK: Default folder viewer

    /// Apps that "Show in Finder" through NSWorkspace honour the NSFileViewer default. Finder keeps the desktop.
    static var isDefaultViewer: Bool {
        (CFPreferencesCopyAppValue("NSFileViewer" as CFString, kCFPreferencesAnyApplication) as? String) == Bundle.main.bundleIdentifier
    }

    static func setDefaultViewer(_ on: Bool) {
        let id = Bundle.main.bundleIdentifier ?? "com.wasteddesigner.kinetik.files"
        let value: CFPropertyList? = on ? id as CFString : nil
        CFPreferencesSetValue("NSFileViewer" as CFString, value, kCFPreferencesAnyApplication, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
        CFPreferencesSynchronize(kCFPreferencesAnyApplication, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
        // Also be the viewer Launch Services picks for folders other apps open; switching off gives them back to Finder.
        LSSetDefaultRoleHandlerForContentType(UTType.folder.identifier as CFString, .viewer, (on ? id : "com.apple.finder") as CFString)
    }
}

// MARK: - Global hotkey (⌥⌘E by default)

/// Registered with Carbon, which needs no Accessibility permission, unlike event taps.
final class Hotkey {
    static let shared = Hotkey()
    private var ref: EventHotKeyRef?
    private var handler: EventHandlerRef?

    func update() {
        unregister()
        guard AppSettings.shared.hotkeyEnabled else { return }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            Task { @MainActor in AppState.shared.showOrCreateWindow() }
            return noErr
        }, 1, &spec, nil, &handler)
        let id = EventHotKeyID(signature: OSType(0x4B4E_4B46), id: 1) // "KNKF"
        RegisterEventHotKey(UInt32(kVK_ANSI_E), UInt32(cmdKey | optionKey), id, GetApplicationEventTarget(), 0, &ref)
    }

    private func unregister() {
        if let ref { UnregisterEventHotKey(ref) }
        if let handler { RemoveEventHandler(handler) }
        ref = nil; handler = nil
    }
}

// MARK: - Quick Look on Space

@MainActor
final class QuickLook: NSObject, QLPreviewPanelDataSource, QLPreviewPanelDelegate {
    static let shared = QuickLook()
    private var urls: [URL] = []

    func toggle(urls: [URL]) {
        guard let panel = QLPreviewPanel.shared() else { return }
        if QLPreviewPanel.sharedPreviewPanelExists() && panel.isVisible {
            panel.orderOut(nil)
            return
        }
        guard !urls.isEmpty else { return }
        self.urls = urls
        panel.dataSource = self
        panel.delegate = self
        panel.reloadData()
        panel.makeKeyAndOrderFront(nil)
    }

    /// Keeps the panel in step with the selection while it's open.
    func selectionChanged(_ urls: [URL]) {
        guard QLPreviewPanel.sharedPreviewPanelExists(), let panel = QLPreviewPanel.shared(), panel.isVisible, !urls.isEmpty else { return }
        self.urls = urls
        panel.reloadData()
    }

    nonisolated func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int {
        MainActor.assumeIsolated { urls.count }
    }

    nonisolated func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! {
        MainActor.assumeIsolated { urls[index] as NSURL }
    }
}
