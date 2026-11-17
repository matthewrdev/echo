import SceneKit
import simd

struct CameraFrustum: Equatable {
    let centre: SIMD3<Double>
    let right: SIMD3<Double>
    let down: SIMD3<Double>
    let forward: SIMD3<Double>
    let halfWidth: Double
    let halfHeight: Double

    init?(camera: CameraPose, photo: Photo) {
        let validPose =
            camera.centre.count == 3 && camera.rotation.count == 9
            && camera.centre.allSatisfy(\.isFinite) && camera.rotation.allSatisfy(\.isFinite)
        let focal = camera.focalPixels > 0 ? camera.focalPixels : photo.focalPixels
        guard validPose, photo.width > 0, photo.height > 0, focal.isFinite, focal > 0 else { return nil }
        centre = SIMD3(camera.centre[0], camera.centre[1], camera.centre[2])
        let r = camera.rotation
        right = SIMD3(r[0], r[3], r[6])
        down = SIMD3(r[1], r[4], r[7])
        forward = SIMD3(r[2], r[5], r[8])
        halfWidth = Double(photo.width) / (2 * focal)
        halfHeight = Double(photo.height) / (2 * focal)
    }

    func contains(_ point: SIMD3<Double>) -> Bool {
        let delta = point - centre
        let depth = simd_dot(forward, delta)
        let insideImage =
            abs(simd_dot(right, delta)) <= depth * halfWidth && abs(simd_dot(down, delta)) <= depth * halfHeight
        return depth > 1e-8 && insideImage
    }

    func plane(_ axis: SIMD3<Double>) -> SCNVector4 {
        SCNVector4(axis.x, axis.y, axis.z, -simd_dot(axis, centre))
    }
}
