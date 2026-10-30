import Foundation

// Multi-level undo for file operations. Each entry is one user action (e.g. "Rename 12 items") made of the individual
// changes it caused. The app undoes an entry by reversing its changes in the opposite order; whatever it actually did
// (for example the trash location it got back) becomes the entry pushed onto the redo stack.

public enum FileChange: Equatable, Sendable {
    /// A rename or move. Undo moves it back.
    case moved(from: URL, to: URL)
    /// A new file or folder (new folder, copy, paste, extract). Undo moves it to the Trash.
    case created(URL)
    /// Moved to the Trash. Undo puts it back where it was.
    case trashed(original: URL, inTrash: URL)
    /// Finder tags changed.
    case tagged(URL, old: [String], new: [String])

    /// The change that reverses this one, when it can be known in advance.
    /// `created` and `trashed` return nil: the app has to perform them to learn the resulting location.
    public var simpleInverse: FileChange? {
        switch self {
        case .moved(let a, let b): return .moved(from: b, to: a)
        case .tagged(let u, let old, let new): return .tagged(u, old: new, new: old)
        case .created, .trashed: return nil
        }
    }
}

public struct UndoEntry: Equatable, Sendable {
    public let title: String
    public let changes: [FileChange]
    public init(_ title: String, _ changes: [FileChange]) { self.title = title; self.changes = changes }
}

public struct UndoHistory: Sendable {
    public private(set) var undoStack: [UndoEntry] = []
    public private(set) var redoStack: [UndoEntry] = []
    public let limit: Int

    public init(limit: Int = 100) { self.limit = limit }

    public var canUndo: Bool { !undoStack.isEmpty }
    public var canRedo: Bool { !redoStack.isEmpty }
    public var undoTitle: String? { undoStack.last?.title }
    public var redoTitle: String? { redoStack.last?.title }

    /// A new action: goes on the undo stack and clears redo.
    public mutating func record(_ entry: UndoEntry) {
        guard !entry.changes.isEmpty else { return }
        undoStack.append(entry)
        if undoStack.count > limit { undoStack.removeFirst(undoStack.count - limit) }
        redoStack.removeAll()
    }

    public mutating func takeUndo() -> UndoEntry? { undoStack.popLast() }
    public mutating func takeRedo() -> UndoEntry? { redoStack.popLast() }

    /// After undoing, what was done to reverse it (so Redo can reverse that).
    public mutating func pushRedo(_ entry: UndoEntry) { if !entry.changes.isEmpty { redoStack.append(entry) } }

    /// After redoing, back onto the undo stack without clearing redo.
    public mutating func pushUndo(_ entry: UndoEntry) { if !entry.changes.isEmpty { undoStack.append(entry) } }
}
