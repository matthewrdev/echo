import AppKit
import SceneKit

struct ArtifactPresentation {
    let id = UUID()
    let title: String
    let detail: String
    var scene: SCNScene?
    var image: NSImage?
    var matches: MatchPresentation?
    var features: FeaturePresentation?
    var sceneIdentity: String = ""
    var sparseInspection: SparseInspectionData?
    var metricScale = false
}
