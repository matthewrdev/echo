import Foundation

struct MetalDepthMetrics: Encodable {
    let device: String
    let pixelCount: Int
    let planeCount: Int
    let viewCount: Int
    let pipelineSetupSeconds: Double
    let bufferUploadSeconds: Double
    let gpuSeconds: [Double]
    let dispatchWallSeconds: [Double]
    let repeatOutputsIdentical: Bool
}
