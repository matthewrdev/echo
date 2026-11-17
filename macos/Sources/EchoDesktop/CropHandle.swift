import SceneKit
import simd

struct CropHandle {
    let kind: CropHandleKind
    let position: SIMD3<Double>

    var point: SCNVector3 { SCNVector3(position.x, position.y, position.z) }

    static func handles(for region: ExclusionRegion) -> [CropHandle] {
        guard region.enabled, region.isValid else { return [] }
        let centre = SIMD3(region.centreX, region.centreY, region.centreZ)
        var handles = [CropHandle(kind: .move, position: centre)]
        func append(_ kind: CropHandleKind, _ offset: SIMD3<Double>) {
            handles.append(CropHandle(kind: kind, position: centre + region.orientation.act(offset)))
        }
        append(.height, SIMD3(0, region.height / 2, 0))
        if region.shape == .box {
            // Lower edges keep the horizontal resize handles clear of the centre in front view.
            append(.width, SIMD3(region.width / 2, -region.height / 2, 0))
            append(.depth, SIMD3(0, -region.height / 2, region.depth / 2))
            append(.rotate, SIMD3(region.width * 0.7, 0, region.depth * 0.7))
        } else {
            append(.radius, SIMD3(region.radius, 0, 0))
        }
        return handles
    }
}
