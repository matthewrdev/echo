import SceneKit

final class SourceCameraSceneView: SCNView {
    var cameraKey: ((NSEvent) -> Bool)?
    private weak var sourceArtifact: SCNNode?
    private weak var sourceGeometry: SCNGeometry?
    private var displayedFrustum: CameraFrustum?

    override var acceptsFirstResponder: Bool { true }

    override func rotate(with event: NSEvent) {
        // Keep the source camera orientation fixed and prevent rotation bubbling to the main viewer.
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        super.mouseDown(with: event)
    }

    override func keyDown(with event: NSEvent) {
        if cameraKey?(event) != true { super.keyDown(with: event) }
    }

    func show(scene source: SCNScene, frustum: CameraFrustum) {
        guard let artifact = source.rootNode.childNode(withName: "artifact", recursively: false) else {
            pointOfView = nil
            scene = nil
            sourceArtifact = nil
            sourceGeometry = nil
            displayedFrustum = nil
            return
        }
        if sourceArtifact === artifact, sourceGeometry === artifact.geometry, displayedFrustum == frustum { return }
        SCNTransaction.begin()
        SCNTransaction.disableActions = true
        defer { SCNTransaction.commit() }
        if sourceArtifact !== artifact || sourceGeometry !== artifact.geometry || scene == nil {
            let preview = SCNScene()
            preview.background.contents = source.background.contents
            // Share saved geometry, but retain the preview graph while browsing cameras.
            preview.rootNode.addChildNode(artifact.clone())
            let camera = SCNNode()
            camera.name = "source-camera-preview"
            camera.camera = SCNCamera()
            preview.rootNode.addChildNode(camera)
            scene = preview
            pointOfView = camera
        }
        sourceArtifact = artifact
        sourceGeometry = artifact.geometry
        displayedFrustum = frustum
        let box = artifact.boundingBox
        let extent = SCNVector3(
            max(abs(box.min.x - frustum.centre.x), abs(box.max.x - frustum.centre.x)),
            max(abs(box.min.y - frustum.centre.y), abs(box.max.y - frustum.centre.y)),
            max(abs(box.min.z - frustum.centre.z), abs(box.max.z - frustum.centre.z)))
        if let camera = pointOfView { SourceCameraViewpoint().update(camera, frustum: frustum, extent: extent) }
    }
}
