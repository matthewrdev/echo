import SceneKit
import SwiftUI

struct SourceCameraViewport: NSViewRepresentable {
    let scene: SCNScene
    let frustum: CameraFrustum
    let cameraKey: (NSEvent) -> Bool

    func makeNSView(context: Context) -> SourceCameraSceneView {
        let view = SourceCameraSceneView(
            frame: .zero, options: [SCNView.Option.preferredRenderingAPI.rawValue: SCNRenderingAPI.metal.rawValue])
        view.allowsCameraControl = false
        view.autoenablesDefaultLighting = false
        view.antialiasingMode = .multisampling4X
        view.setAccessibilityElement(true)
        view.setAccessibilityRole(.image)
        view.setAccessibilityLabel("Virtual view from the selected source camera")
        view.wantsLayer = true
        view.layer?.masksToBounds = true
        return view
    }

    func updateNSView(_ view: SourceCameraSceneView, context: Context) {
        view.cameraKey = cameraKey
        view.show(scene: scene, frustum: frustum)
    }
}
