import Foundation

struct SparseInspectionData {
    let json: ArtifactJSONReader
    let root: URL
    let photos: [String: Photo]
    let cameras: [String: CameraPose]
    let frustums: [String: CameraFrustum]
    let tracks: [SparseTrack]
    let cameraTracks: [String: [Int]]
    let featureFiles: [String: String]

    init(
        root: URL, scene: ReconstructionScene, photos: [Photo], featureFiles: [String: String],
        json: ArtifactJSONReader = ArtifactJSONReader()
    ) {
        self.json = json
        self.root = root
        self.photos = Dictionary(uniqueKeysWithValues: photos.map { ($0.id, $0) })
        cameras = Dictionary(uniqueKeysWithValues: scene.cameras.map { ($0.imageId, $0) })
        frustums = CloudInspectionData(root: root, cameras: scene.cameras, photos: photos).frustums
        self.featureFiles = featureFiles
        var tracks: [SparseTrack] = []
        var lookup: [SparseTrackKey: Int] = [:]
        for point in scene.points {
            guard let a = point.imageA, let b = point.imageB, !a.isEmpty, !b.isEmpty,
                let pa = point.pixelA, let pb = point.pixelB, pa.count == 2, pb.count == 2,
                point.position.count == 3
            else { continue }
            // Bundle output repeats the same 3D track once per anchor/other observation pair.
            // Retain every contributing photograph, without treating these duplicates as new points.
            let key = SparseTrackKey(position: point.position, anchorImage: a, anchorPixel: pa)
            let index: Int
            if let existing = lookup[key] {
                index = existing
            } else {
                index = tracks.count
                lookup[key] = index
                tracks.append(SparseTrack(position: point.position, observations: []))
            }
            for observation in [
                SparseObservation(imageID: a, pixel: pa, error: nil),
                SparseObservation(imageID: b, pixel: pb, error: point.error),
            ] where !tracks[index].observations.contains(where: { $0.imageID == observation.imageID }) {
                tracks[index].observations.append(observation)
            }
        }
        self.tracks = tracks
        var cameraTracks: [String: [Int]] = [:]
        for (index, track) in tracks.enumerated() {
            for observation in track.observations {
                cameraTracks[observation.imageID, default: []].append(index)
            }
        }
        self.cameraTracks = cameraTracks
    }
}
