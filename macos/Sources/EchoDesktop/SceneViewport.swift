import SceneKit
import SwiftUI

struct SceneViewport: NSViewRepresentable {
    let presentation: ArtifactPresentation
    @ObservedObject var controller: SceneViewportController
    var showGround = false
    var selected: SparseSelection?
    var selection: ((SparseSelection?) -> Void)?
    var exclusionRegions: [ExclusionRegion] = []
    var selectedExclusionID: String?
    var placeExclusion: ((SCNVector3) -> Void)?

    func makeNSView(context: Context) -> ArtifactSceneView {
        let view = ArtifactSceneView(
            frame: .zero, options: [SCNView.Option.preferredRenderingAPI.rawValue: SCNRenderingAPI.metal.rawValue])
        controller.view = view
        view.allowsCameraControl = true
        view.autoenablesDefaultLighting = false
        view.renderObserver.orientation = controller.orientation
        view.delegate = view.renderObserver
        view.antialiasingMode = .multisampling4X
        view.defaultCameraController.inertiaEnabled = true
        view.setAccessibilityElement(true)
        view.setAccessibilityRole(.image)
        view.setAccessibilityLabel("3D artifact viewer")
        view.wantsLayer = true
        view.layer?.masksToBounds = true
        return view
    }

    func sizeThatFits(_ proposal: ProposedViewSize, nsView: ArtifactSceneView, context: Context) -> CGSize? {
        CGSize(width: proposal.width ?? 400, height: proposal.height ?? 300)
    }

    func updateNSView(_ view: ArtifactSceneView, context: Context) {
        if view.scene !== presentation.scene {
            let preserveCamera =
                !presentation.sceneIdentity.isEmpty && view.artifactIdentity == presentation.sceneIdentity
            let transform = preserveCamera ? view.pointOfView?.transform : nil
            view.artifactIdentity = presentation.sceneIdentity
            view.scene = presentation.scene
            view.pointOfView = presentation.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
            if let transform { view.pointOfView?.transform = transform }
            view.needsInitialFit = !preserveCamera
            view.needsLayout = true
        }
        view.scene?.rootNode.childNode(withName: "ground-grid", recursively: false)?.isHidden = !showGround
        view.inspection = presentation.sparseInspection
        view.cloudInspection = presentation.cloudInspection
        view.selection = selection
        view.placeExclusion = placeExclusion
        view.exclusionOverlay.update(scene: view.scene, regions: exclusionRegions, selectedID: selectedExclusionID)
        view.highlight(selected)
    }
}
