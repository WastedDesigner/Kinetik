# Kinetik Files for Mac — spec

The Mac version of Kinetik Files: the same file manager as the Windows app, rebuilt as a native macOS app in Swift (SwiftUI with AppKit where SwiftUI falls short). It should feel like a Kinetik app first and a Mac app second: the suite's themes, accents, rounded menus and keyboard-first design, with the Mac conventions people expect (⌘ shortcuts, Finder tags, Quick Look, the Trash, native window tabs).

- **Target:** macOS 14 Sonoma and later, Apple silicon and Intel.
- **Language:** Swift 5.10+, SwiftUI app lifecycle, AppKit via `NSViewRepresentable` for the file list, Quick Look and hotkeys.
- **Build:** a Swift package (`swift build`) plus `bundle.sh`, which wraps the binary into `Kinetik Files.app`. No Xcode project needed, though `open Package.swift` works in Xcode.
- **Distribution:** not sandboxed (a file manager needs the whole disk), so it ships outside the App Store, signed with a Developer ID and notarised, like the Windows build is signed through SignPath.
- **Credit:** Akila Sella Hennedige (@WastedDesigner). GPL-3.0, same as the repo.

## What can't be the same on a Mac

| Windows | Mac | Why |
|---|---|---|
| Replace File Explorer as the default file manager | **Default folder viewer** setting: writes `NSFileViewer` so "Show in Finder" / "Reveal" in apps that honour it (most Cocoa apps, Terminal's `open -R`) opens Kinetik Files. Plus registering as a handler for `public.folder` so double-clicking a folder in other apps' open dialogs and `open /some/folder` can route here. | macOS has no supported way to replace Finder. Finder always owns the desktop, the Dock icon and the Trash window. |
| Win+E opens Kinetik Files | **Global hotkey** (default ⌥⌘E, changeable) registered with Carbon `RegisterEventHotKey`, which needs no Accessibility permission. | Same idea; no reserved shortcut to fight. |
| Windows' right-click menu (shell extensions) | Our own context menu plus a **Services** submenu (`NSMenu` with `NSServices`), **Open With** from `NSWorkspace.urlsForApplications(toOpen:)`, and **Share** (`NSSharingServicePicker`). | macOS has no shell-extension menu to host. Finder Sync extensions only work inside Finder. |
| Colour tags stored by path | **Finder tags** (`URLResourceValues.tagNames`). Tags follow the file everywhere, sync with Finder, iCloud and Spotlight. Kinetik's own colours map onto Finder's seven colours; extra tags keep a Kinetik colour in settings. | Better than Windows: no separate database, survives renames by other apps. |
| Comments | **Finder comments** (`kMDItemFinderComment`, written through `NSAppleScript`/`xattr com.apple.metadata:kMDItemFinderComment`). | Native, shows in Get Info. |
| Recycle Bin | **Trash** via `FileManager.trashItem`, which returns the trashed URL so Undo can put it back. | |
| Windows Search index | **Spotlight** via `NSMetadataQuery` / `MDQuery`, including content search and metadata (`kMDItemPixelWidth`, EXIF, authors, durations). | Faster and richer than the Windows index. |
| Shadow copies (previous versions) | **Time Machine** local snapshots: list with `tmutil listlocalsnapshots`, browse backups under the backup volume. Later phase. | |
| Junctions | Not on macOS. Symbolic links, hard links and **Finder aliases** (`URL.bookmarkData(options: .suitableForBookmarkFile)`). | |
| Open terminal here (Windows Terminal / PowerShell) | Terminal, iTerm2, Warp or Ghostty, chosen in Settings, opened with `NSWorkspace.open([folder], withApplicationAt:)`. | |
| Save/Open dialog strip | A **floating "jump to" panel**: when an Open/Save sheet is front-most, the hotkey shows the open Kinetik folders; picking one types the path into the sheet with ⌘⇧G. Needs Accessibility permission, so it's opt-in. Later phase. | |
| Settings in the registry / portable mode | `UserDefaults` suite `com.wasteddesigner.kinetik` shared by all Kinetik Mac apps, plus JSON stores in `~/Library/Application Support/Kinetik/Files`. Portable mode: a `KinetikFiles.portable` file next to the .app keeps everything beside it. | |
| Windows preview handlers | **Quick Look** (`QLPreviewView` in the preview pane, `QLPreviewPanel` on Space), which covers PDFs, Office, iWork, images, video, audio, fonts, 3D (USDZ) and any app's Quick Look plugin. | |

## Feature parity

Phase 1 is what the starter project in this folder already contains (written but not yet compiled; Claude Code on the Mac finishes and fixes it). Later phases are for Claude Code to build, in order.

### Navigation and interface

| Feature | Phase | Mac notes |
|---|---|---|
| Tabs | 1 | Our own tab strip per window (like the Windows app), ⌘T / ⌘W / ⌃Tab / ⌘1–9. |
| Dual pane, independent folders | 1 | ⌘⌥D toggles; Tab key switches the active pane. |
| Details, grid and Miller columns views | 1 | ⌘1/⌘2/⌘3 inside a pane are taken by tabs on Windows (Ctrl+1–9); on Mac use ⌘⌥1/2/3 for views to match Finder's spirit while keeping ⌘1–9 for tabs. |
| Breadcrumb address bar, type a path, ~ expansion | 1 | Click a crumb to go there, click its chevron for a sibling menu; ⌘L to type. |
| Back / forward / up, history menu | 1 | ⌘[ ⌘] ⌘↑, plus mouse back/forward buttons. |
| Sidebar: favourites, volumes with fill bars, tags | 1 | Volumes from `FileManager.mountedVolumeURLs` with `volumeAvailableCapacityForImportantUsage`. |
| Preview pane with Quick Look, details, hashes | 1 | ⌥⌘P toggles. |
| Quick Look on Space | 1 | `QLPreviewPanel`. |
| Themes and accents shared with the suite | 1 | Match System, Graphite, OLED black, Midnight, Nord, Mocha, Forest, Light, Paper; Sky, Violet, Mint, Amber, Rose, Coral, System accent. Exact RGB values copied from `SuiteSupport.cs`. |
| Compact / normal / roomy rows, ⌘+/⌘− zoom | 1 | |
| Hidden files (⌘⇧.), extensions, folders first | 1 | ⌘⇧. matches Finder. |
| Saved workspaces / tab groups | 2 | JSON in the data folder. |
| Customisable columns | 2 | Kind, size, modified, created, added, tags, dimensions, duration, comments. |
| Per-folder view memory | 2 | |
| Flat view (all files in subfolders) | 2 | ⌘⇧L. |
| Collections (virtual folders) | 2 | Stored as bookmark data so they survive moves. |
| Custom toolbar, menus and shortcuts | 3 | |
| Stacked / multi-panel layouts (more than two panes) | 3 | |

### Search and organisation

| Feature | Phase | Mac notes |
|---|---|---|
| Instant filter as you type (current folder) | 1 | |
| Query language: words, quotes, OR, -not, `ext:`, `size:>10mb`, `modified:<7d`, `kind:`, `tag:`, `/regex/` | 1 | Parser in `KinetikFilesCore/Query.swift`, unit-tested. Same syntax as Windows. |
| Recursive search in a folder | 1 | Background `FileManager.enumerator`, cancellable. |
| Spotlight-indexed and content search | 2 | `NSMetadataQuery` scoped to the folder; `content:` operator. |
| Metadata search (EXIF, dimensions, duration) | 2 | Spotlight attributes. |
| Saved searches | 2 | Also exportable as Finder `.savedSearch`. |
| Finder tags with colours | 1 | Read, add, remove, filter by. |
| Finder comments | 2 | |
| Folder sizes | 1 | Background, cached, shown in the size column. |
| Space map (treemap) | 3 | From the Kinetik feature ideas. |
| Duplicate finder (size → partial hash → full SHA-256) | 2 | |
| Similar images | 3 | `VNGenerateImageFeaturePrintRequest` from Vision; much easier than on Windows. |
| Reports / inventories (CSV, HTML) | 3 | |
| Select matching (⌘⇧S) with rules | 2 | |

### File operations and automation

| Feature | Phase | Mac notes |
|---|---|---|
| Copy, cut, paste, drag and drop, duplicate | 1 | Finder-compatible pasteboard (`NSPasteboard` file URLs), so copy in Finder and paste here works both ways. Cut is our own mark (Finder only has ⌘⌥V "move"); support both ⌘X then ⌘V, and ⌘C then ⌘⌥V. |
| Rename inline (Return), new folder (⌘⇧N), new text file (⌘⌥N) | 1 | |
| Move to Trash (⌘⌫), delete immediately (⌥⌘⌫ with confirm) | 1 | |
| Multi-level undo / redo | 1 | For rename, move, copy, trash, new folder, tags. `KinetikFilesCore/UndoStack.swift`. |
| Batch rename with live preview: find/replace, regex, numbering, case, date tokens | 1 | `KinetikFilesCore/BatchRename.swift`, unit-tested, collision-safe two-step rename. |
| Compress to ZIP / extract here | 1 | `/usr/bin/ditto -c -k --sequesterRsrc --keepParent` and `ditto -x -k`; never extracts over existing files. |
| Paste image or text as a file (⌘⇧V) | 1 | |
| Copy path (⌘⌥C, same as Finder), open terminal here (⌃`), reveal in Finder (⌘⇧R), Get Info (⌘I opens Finder's panel via AppleScript, our own details otherwise) | 1 | |
| File hashes (MD5, SHA-1, SHA-256) | 1 | CryptoKit. |
| Command palette (⌘⇧P / ⌘K) | 1 | Every command searchable. |
| Copy queue: pause, resume, conflict rules, verify with checksums | 2 | Own engine on `FileManager` + `copyfile(3)` with `COPYFILE_CLONE` (instant APFS clones), progress callbacks. |
| Compare and sync folders | 2 | |
| Symbolic links, hard links, aliases | 2 | |
| Custom actions (scripts with $FILE, $DIR placeholders) and Shortcuts.app actions | 2 | Run `shortcuts run "Name" -i file` too, a Mac bonus. |
| Automations (watch folder → rules) | 3 | `DispatchSource` / FSEvents. |
| Split and join | 3 | |
| Secure delete | 3 | With the same SSD caveat; on APFS it's mostly symbolic, say so. |
| Batch image convert / resize | 3 | `sips` or ImageIO. |

### Remote and storage

| Feature | Phase | Mac notes |
|---|---|---|
| iCloud Drive, Dropbox, Google Drive, OneDrive | 1 | These live under `~/Library/CloudStorage` and iCloud's `Mobile Documents`; show them in the sidebar. Download/evict iCloud items with `FileManager.startDownloadingUbiquitousItem` / `evictUbiquitousItem`. |
| SMB / AFP network shares | 2 | `NSWorkspace` mounting via `smb://` URLs, Connect to Server (⌘K is the palette, so use ⌘⇧K). |
| SFTP / FTP / FTPS | 3 | Phase 3; a pure-Swift SFTP library or shell out to `sftp`. |
| Eject volumes | 1 | `NSWorkspace.unmountAndEjectDevice`. |

### Look and feel

- The suite's colours, exactly. Window background = `Bg`, sidebar = `Nav`, rows/cards = `Card`, hover = `Hover`, fields = `Field`, hairlines = `Border`, text = `Text`, secondary = `Sub`, accent.
- Rounded selection pills with an accent tint (like Kinetik Stats 4.1's menus), not full-width bars.
- SF Symbols for UI icons; real file icons from `NSWorkspace.icon(forFile:)`; thumbnails from `QLThumbnailGenerator`.
- The titlebar is hidden and the tab strip sits in it (`.windowStyle(.hiddenTitleBar)`), like Safari/Arc, with the traffic lights left of the tabs.
- Picking a theme writes to the shared defaults suite so other Kinetik Mac apps can follow.

## Data

| What | Where |
|---|---|
| Settings | `UserDefaults(suiteName: "com.wasteddesigner.kinetik")`, keys prefixed `files.` (theme and accent are unprefixed, shared). |
| Favourites, workspaces, collections, saved searches, actions | `~/Library/Application Support/Kinetik/Files/*.json` |
| Folder size cache | `~/Library/Caches/Kinetik/Files/sizes.json` |

## Phases

1. **Starter (this folder):** everything marked 1 above. Get it compiling, then running, then polished.
2. **Power user:** copy queue, Spotlight search, workspaces, columns, duplicates, compare/sync, links, custom actions, network shares, comments.
3. **Extras:** similar images, space map, automations, SFTP, reports, dialog jump panel, Time Machine browser, split/join, secure delete, image batch.
4. **Release:** Developer ID signing and notarisation in GitHub Actions (`macos-14` runner), a `.dmg`, Sparkle updates, README section and real screenshots.
