import SwiftUI
import AppKit

// The Kinetik apps' look: a theme and an accent colour picked once and shared by every Kinetik app on this Mac.
// The RGB values are copied exactly from SuiteSupport.cs so the Mac and Windows apps match.

struct Look: Equatable {
    var name: String
    var light: Bool
    var bg, nav, card, hover, field, border, text, sub, accent, onAccent, warn, good: Color

    static let themes = ["Match System", "Graphite", "OLED black", "Midnight", "Nord", "Mocha", "Forest", "Light", "Paper"]
    static let accents = ["Sky", "Violet", "Mint", "Amber", "Rose", "Coral", "System accent"]

    private static func c(_ r: Double, _ g: Double, _ b: Double) -> Color { Color(.sRGB, red: r / 255, green: g / 255, blue: b / 255) }

    // Page, sidebar, card, card under the mouse, text box, hairline, text, secondary text.
    private static let palettes: [String: [Color]] = [
        "Graphite": [c(17, 17, 19), c(17, 17, 19), c(26, 26, 29), c(34, 34, 38), c(31, 31, 35), c(44, 44, 50), c(236, 236, 240), c(142, 142, 152)],
        "OLED black": [c(0, 0, 0), c(0, 0, 0), c(14, 14, 16), c(24, 24, 27), c(20, 20, 23), c(36, 36, 40), c(242, 242, 245), c(136, 136, 146)],
        "Midnight": [c(13, 18, 32), c(13, 18, 32), c(21, 28, 46), c(28, 37, 59), c(25, 33, 53), c(39, 50, 76), c(230, 235, 245), c(135, 146, 171)],
        "Nord": [c(46, 52, 64), c(46, 52, 64), c(54, 61, 75), c(61, 69, 85), c(58, 65, 80), c(70, 79, 97), c(236, 239, 244), c(163, 171, 186)],
        "Mocha": [c(30, 30, 46), c(30, 30, 46), c(39, 39, 57), c(49, 50, 69), c(45, 46, 64), c(60, 61, 82), c(205, 214, 244), c(147, 153, 178)],
        "Forest": [c(16, 24, 20), c(16, 24, 20), c(24, 34, 29), c(31, 44, 37), c(28, 39, 33), c(42, 58, 49), c(228, 240, 231), c(138, 163, 147)],
        "Light": [c(246, 246, 248), c(246, 246, 248), c(255, 255, 255), c(240, 240, 244), c(255, 255, 255), c(222, 222, 229), c(27, 27, 31), c(104, 104, 116)],
        "Paper": [c(247, 244, 238), c(247, 244, 238), c(253, 251, 247), c(242, 237, 228), c(255, 255, 255), c(226, 219, 206), c(46, 38, 30), c(120, 106, 90)],
    ]

    // Accent on dark themes, accent on light themes.
    private static let accentColours: [String: (dark: (Double, Double, Double), light: (Double, Double, Double))] = [
        "Sky": ((76, 194, 255), (0, 110, 200)),
        "Violet": ((167, 139, 250), (110, 70, 230)),
        "Mint": ((52, 211, 153), (10, 140, 95)),
        "Amber": ((251, 191, 36), (176, 112, 0)),
        "Rose": ((244, 114, 182), (196, 40, 120)),
        "Coral": ((255, 128, 102), (210, 70, 45)),
    ]

    static func make(theme: String, accent: String, systemIsDark: Bool) -> Look {
        var name = theme == "Match System" ? (systemIsDark ? "Graphite" : "Light") : theme
        if palettes[name] == nil { name = "Graphite" }
        let p = palettes[name]!
        let light = name == "Light" || name == "Paper"

        var accentRGB: (Double, Double, Double)
        if accent == "System accent", let ns = NSColor.controlAccentColor.usingColorSpace(.sRGB) {
            accentRGB = (Double(ns.redComponent * 255), Double(ns.greenComponent * 255), Double(ns.blueComponent * 255))
        } else {
            let a = accentColours[accent] ?? accentColours["Sky"]!
            accentRGB = light ? a.light : a.dark
        }
        let luma = (0.2126 * accentRGB.0 + 0.7152 * accentRGB.1 + 0.0722 * accentRGB.2) / 255
        return Look(
            name: theme, light: light,
            bg: p[0], nav: p[1], card: p[2], hover: p[3], field: p[4], border: p[5], text: p[6], sub: p[7],
            accent: c(accentRGB.0, accentRGB.1, accentRGB.2),
            onAccent: luma > 0.55 ? c(17, 17, 19) : .white,
            warn: light ? c(200, 40, 40) : c(255, 110, 110),
            good: light ? c(20, 140, 70) : c(110, 210, 140)
        )
    }
}

/// The theme and accent, shared with the other Kinetik Mac apps through one defaults suite.
final class ThemeStore: ObservableObject {
    static let shared = ThemeStore()
    @Published var theme: String { didSet { Defaults.suite.set(theme, forKey: "Theme"); post() } }
    @Published var accent: String { didSet { Defaults.suite.set(accent, forKey: "Accent"); post() } }

    /// macOS's own light/dark setting, for Match System.
    @Published var systemDark = ThemeStore.readSystemDark()

    static func readSystemDark() -> Bool {
        UserDefaults.standard.string(forKey: "AppleInterfaceStyle")?.lowercased() == "dark"
    }

    private init() {
        theme = Defaults.suite.string(forKey: "Theme") ?? "Graphite"
        accent = Defaults.suite.string(forKey: "Accent") ?? "Sky"
        DistributedNotificationCenter.default().addObserver(forName: .init("AppleInterfaceThemeChangedNotification"), object: nil, queue: .main) { [weak self] _ in
            // The defaults value updates a moment after the notification.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { self?.systemDark = ThemeStore.readSystemDark() }
        }
        // Another Kinetik app changed the theme.
        DistributedNotificationCenter.default().addObserver(forName: .init("com.wasteddesigner.kinetik.look"), object: nil, queue: .main) { [weak self] _ in
            guard let self else { return }
            let t = Defaults.suite.string(forKey: "Theme") ?? "Graphite", a = Defaults.suite.string(forKey: "Accent") ?? "Sky"
            if t != self.theme { self.theme = t }
            if a != self.accent { self.accent = a }
        }
    }

    private var posting = false
    private func post() {
        guard !posting else { return }
        posting = true
        DistributedNotificationCenter.default().postNotificationName(.init("com.wasteddesigner.kinetik.look"), object: nil, userInfo: nil, deliverImmediately: true)
        posting = false
    }

    var look: Look { Look.make(theme: theme, accent: accent, systemIsDark: systemDark) }
}

private struct LookKey: EnvironmentKey {
    static let defaultValue = Look.make(theme: "Graphite", accent: "Sky", systemIsDark: true)
}

extension EnvironmentValues {
    var look: Look {
        get { self[LookKey.self] }
        set { self[LookKey.self] = newValue }
    }
}

/// Puts the current Look into the environment and sets light/dark for native controls to match the theme.
struct Themed: ViewModifier {
    @ObservedObject var store = ThemeStore.shared

    func body(content: Content) -> some View {
        let look = store.look
        content
            .environment(\.look, look)
            .tint(look.accent)
            .preferredColorScheme(look.light ? .light : .dark)
    }
}

extension View {
    func themed() -> some View { modifier(Themed()) }
}
