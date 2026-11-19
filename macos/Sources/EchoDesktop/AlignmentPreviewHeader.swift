import Foundation

struct AlignmentPreviewHeader: Decodable {
    let scene: ReconstructionScene
    let totalPoints: Int
    let images: [String]
}
