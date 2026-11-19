import simd

struct CropRegionGeometry: Sendable {
    private let region: ExclusionRegion
    private let inverseOrientation: simd_quatd
    private let centre: SIMD3<Double>

    init(_ region: ExclusionRegion) {
        self.region = region
        inverseOrientation = region.orientation.inverse
        centre = SIMD3(region.centreX, region.centreY, region.centreZ)
    }

    func contains(_ point: SIMD3<Double>) -> Bool {
        if region.shape == .projectedHull { return region.hull?.contains(point) == true }
        let local = inverseOrientation.act(point - centre)
        guard abs(local.y) <= region.height / 2 else { return false }
        if region.shape == .box {
            return abs(local.x) <= region.width / 2 && abs(local.z) <= region.depth / 2
        }
        let x = local.x / region.radius
        let z = local.z / region.radius
        return x * x + z * z <= 1
    }
}
