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
    var selectSurfacePoint: ((SCNVector3) -> Void)?
    var cropBrush: CropBrushConfiguration?
    var finishCropStroke: ((ExclusionRegion) -> Void)?
    var cropStrokeError: ((String) -> Void)?
    var placeExclusion: ((SCNVector3) -> Void)?
    var changeExclusion: ((ExclusionRegion) -> Void)?
    var texturePainting: TexturePriorityPainting?
    var textureBrush: TextureBrushConfiguration?
    var showTexturePriority = false
    var finishTextureStroke: ((TexturePriorityStroke) -> Void)?

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

    static func dismantleNSView(_ view: ArtifactSceneView, coordinator: ()) {
        view.delegate = nil
        view.cropBrush.cancel(in: view)
        view.cropHandles.update(in: view, regions: [], selectedID: nil, changed: nil)
        view.exclusionOverlay.update(scene: nil, regions: [], selectedID: nil)
        view.textureBrush.configuration = nil
        view.textureBrush.painting = nil
        view.textureBrush.cancel(in: view)
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
        view.cameraInspection = presentation.cameraInspection
        view.selection = selection
        view.selectSurfacePoint = selectSurfacePoint
        view.placeExclusion = placeExclusion
        view.cropBrush.configuration = cropBrush
        view.cropBrush.finished = finishCropStroke
        view.cropBrush.failed = cropStrokeError
        if cropBrush == nil { view.cropBrush.cancel(in: view) }
        view.exclusionOverlay.update(scene: view.scene, regions: exclusionRegions, selectedID: selectedExclusionID)
        view.cropHandles.update(
            in: view, regions: exclusionRegions, selectedID: selectedExclusionID,
            changed: placeExclusion == nil && cropBrush == nil ? changeExclusion : nil)
        view.highlight(texturePainting == nil ? selected : nil)
        view.textureBrush.configuration = textureBrush
        view.textureBrush.painting = texturePainting
        view.textureBrush.showHeatmap = showTexturePriority
        view.textureBrush.finished = finishTextureStroke
        view.textureBrush.refresh(in: view)
    }
}
