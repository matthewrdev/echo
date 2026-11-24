import Foundation

struct DroneSource: Identifiable, Hashable, Sendable {
    let volume: URL
    let cameraFolders: [URL]
    let name: String
    let volumeIdentity: String

    var id: String { volume.path + "|" + volumeIdentity }
}
