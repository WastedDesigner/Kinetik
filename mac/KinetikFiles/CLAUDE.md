# Kinetik Files for Mac — notes for Claude Code

You're finishing the Mac version of Kinetik Files, Akila's file manager. Read `SPEC.md` first: it maps every feature of the Windows app to its Mac equivalent and splits the work into phases.

## State of this folder

The phase 1 starter was written on Linux, **without a Swift compiler**: it has been carefully read through but never compiled or run. Your first job is `swift build`, fixing whatever the compiler finds, then `swift test`, then running the app and fixing what looks or behaves wrong. Keep the structure and conventions below while you do.

## Build, run, test

```bash
cd mac/KinetikFiles
swift build                 # compile
swift test                  # core logic tests (rename, search language, paths, undo)
./bundle.sh                 # build/Kinetik Files.app, ad-hoc signed
open "build/Kinetik Files.app"
```

`open Package.swift` opens it in Xcode if you want SwiftUI previews or the debugger.

## Layout

| Path | What |
|---|---|
| `Sources/KinetikFilesCore/` | Pure Foundation, no AppKit: `BatchRename`, `Query` (search language), `PathTools`, `UndoHistory`. Unit-tested. Keep logic here when it can be. |
| `Sources/KinetikFiles/KinetikFilesApp.swift` | App entry, window scene, the menu bar (built from `AppCommand.all`) |
| `Commands.swift` | Every command with its shortcut. The menus and the ⌘K palette both read this list. Add new commands here. |
| `WindowModel.swift` | A window: tabs, panes, and every command's implementation |
| `PaneModel.swift` | A pane: folder, history, listing, filter/search, folder watching, folder sizes |
| `FileOperations.swift` | Every change to files (copy, move, trash, rename, tags, zip) and undo/redo. Nothing else touches the disk for writes. |
| `FileItem.swift` | The listing row model, sorting, icon cache |
| `Integration.swift` | Opening folders from other apps, the ⌥⌘E hotkey, Quick Look panel, terminal, Finder Get Info, default folder viewer |
| `Theme.swift` | The Kinetik themes and accents (exact RGB from `../../SuiteSupport.cs`), shared through the `com.wasteddesigner.kinetik` defaults suite |
| `Settings.swift` | Settings in the shared defaults suite, data folder, portable mode |
| `Views/` | ContentView (tab strip, status bar), PaneView (toolbar, address bar, drops), FileViews (list, grid, columns, right-click menu), SidebarView, PreviewPane, Dialogs (palette, batch rename, go to, settings) |

## Conventions (match the Windows app)

- **Kinetik look, Mac behaviour.** Use `@Environment(\.look)` colours, never system greys. Rounded accent-tinted selection pills. SF Symbols for UI icons.
- **Plain, friendly wording** in the UI and README, the same voice as the Windows README: short sentences, no jargon, say what happens ("Moves it to the Trash"), British spelling (colour, favourites).
- **Every file change goes through `FileOperations`** and records an `UndoEntry`.
- **Never block the main thread** with disk work: `Task.detached` and hop back.
- **Shortcuts follow Finder** where Finder has one (⌘⌫, ⌘⇧N, ⌘⇧G, ⌘Y, ⌘⌥C, ⌘⇧.), Kinetik's Windows shortcuts translated otherwise.
- Swift 5 language mode, macOS 14+. Prefer SwiftUI; drop to AppKit (`NSViewRepresentable`) when SwiftUI can't do it well.
- Credit and licence: Akila Sella Hennedige, GPL-3.0.

## Known rough edges to check first

1. **Quick Look panel** (`QuickLook` in Integration.swift) sets the panel's data source directly. If macOS logs "has no controller" or the panel ignores arrow keys, move it into a responder that implements `acceptsPreviewPanelControl` / `beginPreviewPanelControl` (an `NSViewRepresentable` around the file list is the natural home).
2. **Inline rename** is a SwiftUI alert for now. The Windows app renames in place; on Mac, an `NSTableView` (wrapping the list in `NSViewRepresentable`) would give in-place editing, type-to-select and spring-loaded folders. Worth doing once phase 1 runs.
3. **⌘L** is a hidden button per address bar; with two panes both register it. Route it through the window instead.
4. **The tab strip** sits under the hidden title bar with 74 pt left for the traffic lights. Check spacing on a real window.
5. **Full Disk Access:** without it, Desktop/Documents/Downloads trigger a permission prompt each and some folders error. The pane shows a button to the right settings page.
6. **Default folder viewer** (`Integration.setDefaultViewer`) writes `NSFileViewer` and the `public.folder` viewer role. Test that turning it off really gives everything back to Finder before shipping it.

## Next (phase 2), in order

Copy queue panel with pause/resume and conflict rules → Spotlight search (`NSMetadataQuery`, `content:` operator, metadata) → saved workspaces → customisable list columns → duplicates finder → compare and sync → links and aliases → custom actions → network shares → Finder comments. Details are in `SPEC.md`.
