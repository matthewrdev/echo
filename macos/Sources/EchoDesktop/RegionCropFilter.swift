import simd

struct RegionCropFilter: Sendable {
    private let includes: [CropRegionGeometry]
    private let excludes: [CropRegionGeometry]

    init(_ regions: [ExclusionRegion]) {
        let active = regions.filter { $0.enabled && $0.isValid }
        includes = active.filter { $0.mode == .include }.map(CropRegionGeometry.init)
        excludes = active.filter { $0.mode == .exclude }.map(CropRegionGeometry.init)
    }

    func isExcluded(_ point: SIMD3<Double>) -> Bool {
        if excludes.contains(where: { $0.contains(point) }) { return true }
        return !includes.isEmpty && !includes.contains(where: { $0.contains(point) })
    }
}
