struct FusionPresentationInfo {
    let mode: FusionViewMode
    let samples: Int
    let voxelSize: Double
    let metricScale: Bool
    var preferredSlices: [String: Double] = [:]
    var slicePosition: Double?
    var sliceSamples: Int?

    var units: String { metricScale ? "m" : "scene units" }
    var spacing: String { "\(voxelSize.formatted(.number.precision(.significantDigits(3)))) \(units)" }
}
