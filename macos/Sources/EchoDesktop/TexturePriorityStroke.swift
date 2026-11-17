import Foundation

struct TexturePriorityStroke: Codable, Equatable, Sendable {
    var weight: Double
    var samples: [TextureBrushSample] = []

    var isValid: Bool {
        weight.isFinite && (0.25...4).contains(weight) && samples.allSatisfy(\.isValid)
    }
}
