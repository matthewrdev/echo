import AppKit
import SceneKit

struct ArtifactPresentation {
    let id = UUID()
    let title: String
    let detail: String
    var scene: SCNScene?
    var image: NSImage?
    var depthImage: DepthImagePresentation?
    var matches: MatchPresentation?
    var features: FeaturePresentation?
    var sceneIdentity: String = ""
    var sparseInspection: SparseInspectionData?
    var cameraInspection: CameraInspectionData?
    var metricScale = false
    var fusion: FusionPresentationInfo?
}
