# Kinetik Files for Mac

The Mac version of Kinetik Files: tabs, two panes side by side, Miller columns, a Quick Look preview pane, Finder tags, batch rename with a live preview, a search language, undo for everything, and the Kinetik apps' themes. It's a native Mac app for macOS 14 Sonoma and later.

> **Early work in progress.** This is the first version and hasn't been released yet. See [SPEC.md](SPEC.md) for what's planned.

## Build it

You need a Mac with the Xcode command line tools (`xcode-select --install`).

```bash
cd mac/KinetikFiles
./bundle.sh
open "build/Kinetik Files.app"
```

## Finder and Kinetik Files

macOS doesn't let any app fully replace Finder: Finder always runs the desktop, the Trash and its Dock icon. What Kinetik Files can do, from **Settings → General**:

- **Open folders and "Show in Finder" with Kinetik Files.** Apps that reveal files through macOS (most of them) open Kinetik Files instead. Turn it off to give everything back to Finder.
- **⌥⌘E opens Kinetik Files from anywhere**, like Win+E on Windows.
- **Services → Open in Kinetik Files** works in any app's right-click menu.

## Shortcuts

| Action | Shortcut |
|---|---|
| Command palette | ⌘K |
| New tab / close tab / tab 1–9 | ⌘T / ⌘W / ⌘1–⌘9 |
| Two panes / switch pane | ⌥⌘D / ⌥⇥ |
| List / grid / columns | ⌥⌘1 / ⌥⌘2 / ⌥⌘3 |
| Back / forward / enclosing folder | ⌘[ / ⌘] / ⌘↑ |
| Open | ⌘↓ or double-click |
| Rename / batch rename | Return / ⌘⇧R |
| Quick Look | Space or ⌘Y |
| New folder / new text file | ⌘⇧N / ⌥⌘N |
| Move to Trash / delete immediately | ⌘⌫ / ⌥⌘⌫ |
| Paste / move here / paste as file | ⌘V / ⌥⌘V / ⌘⇧V |
| Copy path | ⌥⌘C |
| Go to folder | ⌘⇧G |
| Filter or search | ⌘F |
| Show hidden files | ⌘⇧. |
| Open Terminal here | ⌃` |
| Undo / redo | ⌘Z / ⌘⇧Z |

## Searching

Type in the filter box. Turn on the stacked-squares button to search subfolders too.

| Type | Finds |
|---|---|
| `holiday photos` | both words in the name |
| `"holiday photos"` | the exact phrase |
| `cat OR dog`, `-draft` | either word; not this word |
| `*.psd`, `/^IMG_\d+/` | wildcards; a regular expression |
| `ext:jpg,png`, `kind:image` | by extension or kind |
| `size:>10mb`, `size:1mb..1gb` | by size |
| `modified:<7d`, `modified:>2026-01-01` | by date (also `created:` and `added:`) |
| `tag:red` | Finder tag |
| `content:"some text"` | inside text files |
