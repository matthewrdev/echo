import Foundation

final class CachedJSONArtifact {
    let version: ArtifactFileVersion
    let value: Any

    init(version: ArtifactFileVersion, value: Any) {
        self.version = version
        self.value = value
    }
}
