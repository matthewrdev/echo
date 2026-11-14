import Foundation

struct MetalDepthFixtureInfo: Decodable {
    let width: Int
    let height: Int
    let pixelCount: Int
    let planeCount: Int
    let viewCount: Int
    let imageId: String
    let cpuSeconds: [Double]
}
