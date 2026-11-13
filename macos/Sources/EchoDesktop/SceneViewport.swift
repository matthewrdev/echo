import SceneKit
import SwiftUI

struct SceneViewport: NSViewRepresentable {
    let presentation: ArtifactPresentation

    func makeNSView(context: Context) -> ArtifactSceneView {
        let view = ArtifactSceneView(
            frame: .zero, options: [SCNView.Option.preferredRenderingAPI.rawValue: SCNRenderingAPI.metal.rawValue])
        view.allowsCameraControl = true
        view.autoenablesDefaultLighting = true
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
        guard view.scene !== presentation.scene else { return }
        let preserveCamera = !presentation.sceneIdentity.isEmpty && view.artifactIdentity == presentation.sceneIdentity
        let transform = preserveCamera ? view.pointOfView?.transform : nil
        view.artifactIdentity = presentation.sceneIdentity
        view.scene = presentation.scene
        view.pointOfView = presentation.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
        if let transform { view.pointOfView?.transform = transform }
    }
}
