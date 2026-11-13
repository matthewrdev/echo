import SceneKit

struct SceneOrientationFrame: Equatable {
    let east: SIMD3<Float>
    let up: SIMD3<Float>
    let north: SIMD3<Float>

    init(camera: SCNNode) {
        let view = simd_inverse(camera.simdWorldTransform)
        east = SIMD3(view.columns.0.x, view.columns.0.y, view.columns.0.z)
        up = SIMD3(view.columns.1.x, view.columns.1.y, view.columns.1.z)
        north = -SIMD3(view.columns.2.x, view.columns.2.y, view.columns.2.z)
    }
}
