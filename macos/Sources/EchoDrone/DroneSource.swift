import Foundation

package struct DroneSource: Identifiable, Hashable, Sendable {
    package let volume: URL
    let cameraFolders: [URL]
    package let name: String
    let volumeIdentity: String

    package var id: String { volume.path + "|" + volumeIdentity }
}
