import SceneKit
import simd

package struct CalibratedRenderCamera: Codable {
    let imageId: String
    let centre: [Double]
    let rotation: [Double]
    let focalPixels: Double
    let photoWidth: Int
    let photoHeight: Int

    func viewport(in size: CGSize) -> CGSize {
        let scale = min(size.width / Double(photoWidth), size.height / Double(photoHeight))
        return CGSize(
            width: max(1, floor(Double(photoWidth) * scale)), height: max(1, floor(Double(photoHeight) * scale)))
    }

    func validate() throws {
        guard centre.count == 3, centre.allSatisfy(\.isFinite),
            rotation.count == 9, rotation.allSatisfy(\.isFinite),
            focalPixels.isFinite, focalPixels > 0, photoWidth > 0, photoHeight > 0
        else { throw ModelRenderError(message: "Invalid calibration for camera \(imageId).") }
    }

    func configure(_ node: SCNNode, size: CGSize, bounds: SceneBounds) {
        guard let camera = node.camera else { return }
        // Echo camera axes are right, down, forward. SceneKit uses right, up, backward.
        let r = rotation.map(Float.init)
        node.position = SCNVector3(centre[0], centre[1], centre[2])
        node.simdOrientation = simd_quatf(
            simd_float3x3(
                columns: (
                    SIMD3(r[0], r[3], r[6]), SIMD3(-r[1], -r[4], -r[7]), SIMD3(-r[2], -r[5], -r[8])
                )))
        let scale = min(size.width / Double(photoWidth), size.height / Double(photoHeight))
        camera.usesOrthographicProjection = false
        camera.projectionDirection = .vertical
        camera.fieldOfView = 2 * atan(size.height / (2 * focalPixels * scale)) * 180 / .pi
        let distance = simd_distance(
            node.simdPosition, SIMD3(Float(bounds.centre.x), Float(bounds.centre.y), Float(bounds.centre.z)))
        camera.zNear = max(0.0001, bounds.span / 100_000)
        camera.zFar = max(1000, Double(distance) + bounds.span * 2)
    }
}
