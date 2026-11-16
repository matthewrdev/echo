import Foundation

struct ReconstructionArtifactMetadata: Decodable {
    let frame: String
    let origin: GeoPosition?
    let cameras: [CameraPose]
    let unplaced: [String]
    let warnings: [String]
}
