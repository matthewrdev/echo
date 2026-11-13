import Foundation

struct FusedVolume: Decodable {
    let voxelSize: Double
    let samples: [VolumeSample]
}
