import SceneKit

final class SourceCameraSceneView: SCNView {
    var cameraKey: ((NSEvent) -> Bool)?
    private weak var sourceArtifact: SCNNode?
    private weak var sourceGeometry: SCNGeometry?
    private var displayedFrustum: CameraFrustum?

    override var acceptsFirstResponder: Bool { true }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        super.mouseDown(with: event)
    }

    override func keyDown(with event: NSEvent) {
        if cameraKey?(event) != true { super.keyDown(with: event) }
    }

    func show(scene source: SCNScene, frustum: CameraFrustum) {
        guard let artifact = source.rootNode.childNode(withName: "artifact", recursively: false) else {
            scene = nil
            sourceArtifact = nil
            return
        }
        if sourceArtifact === artifact, sourceGeometry === artifact.geometry, displayedFrustum == frustum { return }
        sourceArtifact = artifact
        sourceGeometry = artifact.geometry
        displayedFrustum = frustum
        let preview = SCNScene()
        preview.background.contents = source.background.contents
        // Share geometry and textures with the main viewer; only the camera changes.
        preview.rootNode.addChildNode(artifact.clone())
        let box = artifact.boundingBox
        let extent = SCNVector3(
            max(abs(box.min.x - frustum.centre.x), abs(box.max.x - frustum.centre.x)),
            max(abs(box.min.y - frustum.centre.y), abs(box.max.y - frustum.centre.y)),
            max(abs(box.min.z - frustum.centre.z), abs(box.max.z - frustum.centre.z)))
        let camera = SourceCameraViewpoint().make(frustum: frustum, extent: extent)
        preview.rootNode.addChildNode(camera)
        scene = preview
        pointOfView = camera
    }
}
