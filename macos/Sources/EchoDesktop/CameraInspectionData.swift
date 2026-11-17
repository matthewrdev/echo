import Foundation

struct CameraInspectionData {
    let root: URL
    let photos: [String: Photo]
    let cameras: [String: CameraPose]
    let cameraOrder: [String]
    let frustums: [String: CameraFrustum]

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
        cameraOrder = photos.map(\.id).filter { validCameras[$0] != nil }
        self.frustums = frustums
    }
}
