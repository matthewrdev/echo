import Foundation

struct FusionVolumeBounds {
    let minimum: FusionGridPoint
    let maximum: FusionGridPoint
    let preferredSlices: [String: Double]

    init(volume: FusedVolume) throws {
        let validSpacing = volume.voxelSize.isFinite && volume.voxelSize > 0
        guard validSpacing, !volume.samples.isEmpty else {
            throw CLIError(message: "The saved fusion volume is empty or has an invalid grid spacing.")
        }
        var low = SIMD3<Int>(repeating: Int.max)
        var high = SIMD3<Int>(repeating: Int.min)
        var planes = [[Int: Int]](repeating: [:], count: 3)
        for (index, sample) in volume.samples.enumerated() {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let coordinates = [sample.x, sample.y, sample.z]
            let supported = sample.distance.isFinite && sample.views >= 2
            guard supported,
                coordinates.allSatisfy({ $0 > Int32.min && $0 < Int32.max })
            else { throw CLIError(message: "The saved fusion volume contains an invalid sample.") }
            for axis in 0..<3 {
                low[axis] = min(low[axis], coordinates[axis])
                high[axis] = max(high[axis], coordinates[axis])
                planes[axis][coordinates[axis], default: 0] += 1
            }
        }
        minimum = FusionGridPoint(x: low.x, y: low.y, z: low.z)
        maximum = FusionGridPoint(x: high.x, y: high.y, z: high.z)
        var preferred: [String: Double] = [:]
        for (axis, name) in ["X", "Y", "Z"].enumerated() {
            let densest =
                planes[axis].max { left, right in
                    left.value == right.value ? left.key > right.key : left.value < right.value
                }?.key ?? low[axis]
            preferred[name] = Double(densest - low[axis]) / Double(max(1, high[axis] - low[axis]))
        }
        preferredSlices = preferred
    }

    func sliceIndex(axis: String, fraction: Double) -> Int {
        let low = minimum.coordinate(axis)
        let high = maximum.coordinate(axis)
        let fraction = fraction.isFinite ? min(1, max(0, fraction)) : 0.5
        return low + Int((Double(high - low) * fraction).rounded())
    }
}
