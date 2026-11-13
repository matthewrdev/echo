import Foundation

struct ReconstructionScene: Decodable {
    let frame: String
    let origin: GeoPosition?
    let cameras: [CameraPose]
    let points: [CloudPoint]
    let unplaced: [String]
    let warnings: [String]
}
