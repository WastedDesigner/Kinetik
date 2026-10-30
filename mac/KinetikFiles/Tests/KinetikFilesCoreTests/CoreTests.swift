import XCTest
@testable import KinetikFilesCore

final class BatchRenameTests: XCTestCase {
    let dir = URL(fileURLWithPath: "/tmp/k")

    func items(_ names: [String]) -> [RenameItem] { names.map { RenameItem(url: dir.appendingPathComponent($0)) } }

    func testPatternNumbering() {
        var o = RenameOptions(); o.pattern = "Holiday [N:3]"
        let p = BatchRename.plan(items(["a.jpg", "b.JPG", "c.png"]), o)
        XCTAssertEqual(p.map(\.newName), ["Holiday 001.jpg", "Holiday 002.JPG", "Holiday 003.png"])
        XCTAssertTrue(p.allSatisfy { $0.problem == nil })
    }

    func testFindReplaceRegexAndCase() {
        var o = RenameOptions(); o.find = #"IMG_(\d+)"#; o.replace = "Photo $1"; o.useRegex = true; o.caseChange = .lower
        XCTAssertEqual(BatchRename.plan(items(["IMG_0042.HEIC"]), o)[0].newName, "photo 0042.HEIC")
    }

    func testDuplicatesAndBadNames() {
        var o = RenameOptions(); o.pattern = "same"
        let p = BatchRename.plan(items(["a.txt", "b.txt"]), o)
        XCTAssertTrue(p.allSatisfy { $0.problem != nil })
        o.pattern = "a/b"
        XCTAssertNotNil(BatchRename.plan(items(["x.txt"]), o)[0].problem)
    }

    func testSwapUsesTemporaryNames() {
        var o = RenameOptions(); o.find = "a"; o.replace = "b"
        // a -> b while b -> a would collide without the two-step plan.
        let plan = [
            RenamePlanEntry(source: dir.appendingPathComponent("a"), newName: "b", problem: nil),
            RenamePlanEntry(source: dir.appendingPathComponent("b"), newName: "a", problem: nil),
        ]
        let moves = BatchRename.moves(for: plan, token: "t")!
        XCTAssertEqual(moves.count, 4)
        XCTAssertTrue(moves[0].to.lastPathComponent.hasPrefix(".kinetik-rename-t"))
        XCTAssertEqual(moves[3].to.lastPathComponent, "a")
        _ = o
    }

    func testExistingOnDisk() {
        var o = RenameOptions(); o.pattern = "taken"
        let p = BatchRename.plan(items(["a.txt"]), o) { $0.lastPathComponent == "taken.txt" }
        XCTAssertNotNil(p[0].problem)
    }
}

final class QueryTests: XCTestCase {
    let now = ISO8601DateFormatter().date(from: "2026-10-30T12:00:00Z")!
    var cal: Calendar { var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: "UTC")!; return c }

    func f(_ name: String, size: Int64 = 1000, daysAgo: Double = 1, tags: [String] = [], folder: Bool = false) -> FileFacts {
        FileFacts(name: name, isFolder: folder, size: size, modified: now.addingTimeInterval(-daysAgo * 86400), tags: tags)
    }

    func q(_ s: String) -> Query { Query.parse(s, now: now, calendar: cal) }

    func testWordsAndOrAndNot() {
        XCTAssertTrue(q("holiday photo").matches(f("Holiday Photo 1.jpg")))
        XCTAssertFalse(q("holiday photo").matches(f("Holiday.jpg")))
        XCTAssertTrue(q("cat OR dog").matches(f("dog.png")))
        XCTAssertTrue(q("cat | dog").matches(f("cat.png")))
        XCTAssertFalse(q("report -draft").matches(f("report draft.docx")))
        XCTAssertTrue(q("(cat OR dog) png").matches(f("cat.png")))
        XCTAssertFalse(q("(cat OR dog) png").matches(f("cat.jpg")))
    }

    func testOperators() {
        XCTAssertTrue(q("ext:jpg,png").matches(f("a.PNG")))
        XCTAssertTrue(q("size:>10mb").matches(f("big.mov", size: 20_000_000)))
        XCTAssertFalse(q("size:>10mb").matches(f("small.mov", size: 2_000)))
        XCTAssertTrue(q("size:1kb..1mb").matches(f("x", size: 5000)))
        XCTAssertTrue(q("modified:<7d").matches(f("new", daysAgo: 2)))
        XCTAssertFalse(q("modified:<7d").matches(f("old", daysAgo: 30)))
        XCTAssertTrue(q("modified:>2026-10-01").matches(f("x", daysAgo: 3)))
        XCTAssertTrue(q("modified:today").matches(f("x", daysAgo: 0.1)))
        XCTAssertTrue(q("kind:image").matches(f("a.heic")))
        XCTAssertTrue(q("kind:folder").matches(f("Stuff", folder: true)))
        XCTAssertTrue(q("tag:red").matches(f("a", tags: ["Red"])))
    }

    func testWildcardsAndRegex() {
        XCTAssertTrue(q("*.psd").matches(f("Poster.PSD")))
        XCTAssertFalse(q("*.psd").matches(f("Poster.psd.bak")))
        XCTAssertTrue(q(#"/^IMG_\d{4}/"#).matches(f("IMG_1234.jpg")))
        XCTAssertNotNil(q("/[/").error)
    }

    func testContentTermsAndErrors() {
        XCTAssertEqual(q(#"content:"hello world" ext:txt"#).contentTerms, ["hello world"])
        XCTAssertNotNil(q("size:huge").error)
        XCTAssertNotNil(q("(a").error)
        XCTAssertTrue(q("").isEmpty)
        XCTAssertTrue(q("10:30").matches(f("Meeting 10:30.txt")))
    }
}

final class PathToolsTests: XCTestCase {
    func testResolve() {
        let home = URL(fileURLWithPath: "/Users/akila")
        XCTAssertEqual(PathTools.resolve("~/Desktop", relativeTo: nil, home: home)?.path, "/Users/akila/Desktop")
        XCTAssertEqual(PathTools.resolve("sub", relativeTo: home, home: home)?.path, "/Users/akila/sub")
        XCTAssertEqual(PathTools.resolve("\"/tmp\"", relativeTo: nil, home: home)?.path, "/tmp")
    }

    func testUniqueName() {
        let folder = URL(fileURLWithPath: "/x")
        let taken: Set<String> = ["/x/Report.pdf", "/x/Report 2.pdf", "/x/Report copy.pdf"]
        let exists: (URL) -> Bool = { taken.contains($0.path) }
        XCTAssertEqual(PathTools.uniqueName("Report.pdf", in: folder, exists: exists), "Report 3.pdf")
        XCTAssertEqual(PathTools.uniqueName("New.pdf", in: folder, exists: exists), "New.pdf")
        XCTAssertEqual(PathTools.uniqueName("Report.pdf", in: folder, copySuffix: true, exists: exists), "Report copy 2.pdf")
    }

    func testCrumbsAndSize() {
        XCTAssertEqual(PathTools.crumbs(URL(fileURLWithPath: "/a/b")).map(\.path), ["/", "/a", "/a/b"])
        XCTAssertEqual(PathTools.size(1_500_000), "1.5 MB")
        XCTAssertEqual(PathTools.size(1), "1 byte")
        XCTAssertTrue(PathTools.isInside(URL(fileURLWithPath: "/a/b/c"), URL(fileURLWithPath: "/a/b")))
        XCTAssertFalse(PathTools.isInside(URL(fileURLWithPath: "/a/bc"), URL(fileURLWithPath: "/a/b")))
    }
}

final class UndoHistoryTests: XCTestCase {
    func testRecordUndoRedo() {
        var h = UndoHistory(limit: 2)
        let a = URL(fileURLWithPath: "/a"), b = URL(fileURLWithPath: "/b")
        h.record(UndoEntry("Rename", [.moved(from: a, to: b)]))
        XCTAssertEqual(h.undoTitle, "Rename")
        let e = h.takeUndo()!
        h.pushRedo(UndoEntry(e.title, e.changes.compactMap(\.simpleInverse)))
        XCTAssertTrue(h.canRedo)
        h.record(UndoEntry("New Folder", [.created(a)]))
        XCTAssertFalse(h.canRedo, "a new action clears redo")
        h.record(UndoEntry("x", [.created(b)])); h.record(UndoEntry("y", [.created(b)]))
        XCTAssertEqual(h.undoStack.count, 2, "limit keeps the newest")
    }
}
