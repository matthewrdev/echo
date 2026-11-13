import Foundation

struct VolumeSample: Decodable {
    let x: Int
    let y: Int
    let z: Int
    let distance: Float
    let views: Int
}
