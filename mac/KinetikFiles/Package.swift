// swift-tools-version:5.10
// Kinetik Files for Mac. Build with `swift build -c release`, then `./bundle.sh` to make the .app.
import PackageDescription

let package = Package(
    name: "KinetikFiles",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "KinetikFiles", targets: ["KinetikFiles"])
    ],
    targets: [
        // Pure Foundation logic (rename plans, the search language, undo bookkeeping, paths). No AppKit, so it is unit-testable anywhere.
        .target(name: "KinetikFilesCore"),
        // The app: SwiftUI + AppKit.
        .executableTarget(
            name: "KinetikFiles",
            dependencies: ["KinetikFilesCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Quartz"),
                .linkedFramework("QuickLookThumbnailing"),
                .linkedFramework("Carbon"),
                .linkedFramework("CryptoKit")
            ]
        ),
        .testTarget(name: "KinetikFilesCoreTests", dependencies: ["KinetikFilesCore"])
    ]
)
