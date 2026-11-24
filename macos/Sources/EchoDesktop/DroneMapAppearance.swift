import AppKit

struct DroneMapAppearance: Equatable {
    let group: Int?
    let excluded: Bool
    let selected: Bool

    private static let colors: [NSColor] = [
        .systemBlue, .systemOrange, .systemPurple, .systemGreen,
        .systemPink, .systemTeal, .systemRed, .systemIndigo, .systemBrown, .systemYellow,
    ]

    var color: NSColor {
        guard let group, !excluded else { return .systemGray }
        return Self.colors[group % Self.colors.count]
    }
}
