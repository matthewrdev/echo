import Foundation

struct CloudInspectionData {
    let root: URL
    let photos: [String: Photo]
    let cameras: [String: CameraPose]
    let frustums: [String: CameraFrustum]

    var cameraIDs: [String] {
        cameras.keys.sorted {
            let left = photos[$0]?.name ?? $0
            let right = photos[$1]?.name ?? $1
            return left == right ? $0 < $1 : left < right
        }
    }

    init(root: URL, cameras: [CameraPose], photos: [Photo]) {
        self.root = root
        self.photos = Dictionary(uniqueKeysWithValues: photos.map { ($0.id, $0) })
        var validCameras: [String: CameraPose] = [:]
        var frustums: [String: CameraFrustum] = [:]
        for camera in cameras {
            guard let photo = self.photos[camera.imageId], let frustum = CameraFrustum(camera: camera, photo: photo)
            else { continue }
            validCameras[camera.imageId] = camera
            frustums[camera.imageId] = frustum
        }
        self.cameras = validCameras
        self.frustums = frustums
    }
}
