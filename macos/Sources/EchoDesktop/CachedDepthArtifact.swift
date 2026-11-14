import Foundation

struct CachedDepthArtifact {
    let files: [ArtifactFileVersion]
    let depth: DepthImagePresentation
    let confidence: DepthImagePresentation
}
