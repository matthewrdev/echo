import Foundation

struct Surface: Decodable {
    let vertices: [[Double]]
    let faces: [[Int]]
    let sourceImages: [String]
}
