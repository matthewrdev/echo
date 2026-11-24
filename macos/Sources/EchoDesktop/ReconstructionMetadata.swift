import EchoRendering
import Foundation

// Image viewers need camera order and scale, not the reconstructed point cloud.
struct ReconstructionMetadata: Decodable {
    let frame: String
    let origin: GeoPosition?
    let cameras: [CameraPose]

    var isMetric: Bool { origin != nil && !frame.contains("arbitrary") }
}
