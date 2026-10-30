import SwiftUI
import AppKit

@main
struct KinetikFilesApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        WindowGroup(id: "main", for: URL.self) { $url in
            WindowRoot(start: url)
        } defaultValue: {
            URL(fileURLWithPath: AppSettings.shared.homeFolder)
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1180, height: 720)
        .commands { MenuCommands() }

        Settings {
            SettingsView().themed()
        }
    }
}

/// Creates the window's model and hands AppState a way to open new windows.
private struct WindowRoot: View {
    let start: URL
    @StateObject private var model: WindowModel
    @Environment(\.openWindow) private var openWindow

    init(start: URL) {
        self.start = start
        _model = StateObject(wrappedValue: WindowModel(url: start))
    }

    var body: some View {
        ContentView()
            .environmentObject(model)
            .focusedSceneObject(model)
            .themed()
            .background(WindowAccessor { win in
                // Our own tabs replace macOS window tabs.
                win.tabbingMode = .disallowed
                win.titlebarAppearsTransparent = true
                win.isMovableByWindowBackground = false
                AppState.shared.watchKey(win, model)
            })
            .onAppear {
                AppState.shared.register(model)
                AppState.shared.openWindow = { url in openWindow(id: "main", value: url) }
                if let first = AppState.shared.takePending() { model.go(to: first) }
            }
    }
}

/// Hands back the NSWindow a SwiftUI view lives in.
struct WindowAccessor: NSViewRepresentable {
    let found: (NSWindow) -> Void

    func makeNSView(context: Context) -> NSView {
        let v = NSView()
        DispatchQueue.main.async { if let w = v.window { found(w) } }
        return v
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

/// The menu bar. Every item comes from AppCommand.all.
struct MenuCommands: Commands {
    @FocusedObject private var window: WindowModel?

    private func item(_ id: String) -> some View {
        let c = AppCommand.find(id)!
        return Group {
            if let key = c.key {
                Button(c.title) { c.perform(in: window) }.keyboardShortcut(key, modifiers: c.modifiers)
            } else {
                Button(c.title) { c.perform(in: window) }
            }
        }
    }

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            item("newWindow"); item("newTab"); item("openTab")
            Divider()
            item("newFolder"); item("newFolderWith"); item("newText")
            Divider()
            item("open"); item("rename"); item("batchRename"); item("duplicate")
            item("compress"); item("extract")
            Divider()
            item("getInfo"); item("quickLook"); item("reveal"); item("terminal"); item("favourite")
            Divider()
            item("trash"); item("delete")
            Divider()
            item("closeTab")
        }
        CommandGroup(replacing: .undoRedo) { item("undo"); item("redo") }
        CommandGroup(replacing: .pasteboard) {
            item("cut"); item("copy"); item("paste"); item("move"); item("pasteFile"); item("copyPath")
            Divider()
            item("selectAll"); item("selectNone"); item("invert")
        }
        CommandGroup(replacing: .textEditing) { item("find"); item("clearTags") }
        CommandGroup(before: .toolbar) {
            item("list"); item("grid"); item("columns")
            Divider()
            item("dual"); item("switchPane"); item("copyOther"); item("moveOther")
            Divider()
            item("sidebar"); item("preview"); item("hidden")
            Divider()
            item("zoomIn"); item("zoomOut"); item("zoomReset"); item("reload")
            Divider()
            item("palette")
        }
        CommandMenu("Go") {
            item("back"); item("forward"); item("up")
            Divider()
            item("home"); item("desktop"); item("documents"); item("downloads"); item("applications"); item("icloud")
            Divider()
            item("goTo")
            Divider()
            item("nextTab"); item("prevTab")
            ForEach(1..<10) { n in
                Button(n == 9 ? "Last Tab" : "Tab \(n)") { window?.selectTab(number: n) }
                    .keyboardShortcut(KeyEquivalent(Character(String(n))), modifiers: .command)
            }
        }
    }
}
