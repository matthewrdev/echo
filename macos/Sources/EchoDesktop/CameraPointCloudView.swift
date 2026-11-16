import SceneKit
import SwiftUI

struct CameraPointCloudView: NSViewRepresentable {
    let scene: SCNScene
    let frustum: CameraFrustum

    func makeNSView(context: Context) -> SCNView {
        let view = SCNView(
            frame: .zero, options: [SCNView.Option.preferredRenderingAPI.rawValue: SCNRenderingAPI.metal.rawValue])
        view.allowsCameraControl = false
        view.autoenablesDefaultLighting = false
        view.antialiasingMode = .multisampling4X
        view.setAccessibilityElement(true)
        view.setAccessibilityRole(.image)
        view.setAccessibilityLabel("Point cloud from the selected source camera")
        view.wantsLayer = true
        view.layer?.masksToBounds = true
        return view
    }

    func updateNSView(_ view: SCNView, context: Context) {
        guard let artifact = scene.rootNode.childNode(withName: "artifact", recursively: false) else {
            view.scene = nil
            return
        }
        let preview = SCNScene()
        preview.background.contents = scene.background.contents
        // Cloning the node shares its geometry and GPU sources, without copying the point cloud.
        preview.rootNode.addChildNode(artifact.clone())
        let box = artifact.boundingBox
        let extent = SCNVector3(
            max(abs(box.min.x - frustum.centre.x), abs(box.max.x - frustum.centre.x)),
            max(abs(box.min.y - frustum.centre.y), abs(box.max.y - frustum.centre.y)),
            max(abs(box.min.z - frustum.centre.z), abs(box.max.z - frustum.centre.z)))
        let camera = SourceCameraViewpoint().make(frustum: frustum, extent: extent)
        preview.rootNode.addChildNode(camera)
        view.scene = preview
        view.pointOfView = camera
    }
}
