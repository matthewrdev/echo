import Foundation

struct CameraPose: Decodable {
    let imageId: String
    let centre: [Double]
    let rotation: [Double]
    let focalPixels: Double
    let source: String
}
