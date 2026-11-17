import Foundation

struct TexturePriorityPainting: Codable, Equatable, Sendable {
    var alignmentArtifact: String
    var strokes: [TexturePriorityStroke] = []

    var isValid: Bool {
        !alignmentArtifact.isEmpty && strokes.allSatisfy(\.isValid)
    }
}
