import SceneKit

final class ArtifactSceneView: SCNView {
    let gridVisibility = GroundGridVisibility()
    var artifactIdentity = ""
    var needsInitialFit = true
    var inspection: SparseInspectionData?
    var selection: ((Int) -> Void)?
    private var mouseStart: NSPoint?

    override func layout() {
        super.layout()
        if needsInitialFit, bounds.width > 1, bounds.height > 1, scene != nil {
            fitArtifact()
        }
    }

    func fitArtifact() {
        guard let scene, let camera = pointOfView else { return }
        guard let artifact = scene.rootNode.childNode(withName: "artifact", recursively: false) else { return }
        let box = artifact.boundingBox
        let fit = SceneBounds(positions: [
            [box.min.x, box.min.y, box.min.z], [box.max.x, box.max.y, box.max.z],
        ])
        defaultCameraController.stopInertia()
        fit.fit(camera: camera, size: bounds.size)
        defaultCameraController.target = fit.centre
        needsInitialFit = false
    }

    override func mouseDown(with event: NSEvent) {
        mouseStart = convert(event.locationInWindow, from: nil)
        super.mouseDown(with: event)
    }

    override func mouseUp(with event: NSEvent) {
        super.mouseUp(with: event)
        let location = convert(event.locationInWindow, from: nil)
        guard let start = mouseStart, hypot(location.x - start.x, location.y - start.y) < 4,
            let inspection, selection != nil
        else { return }
        var nearest: Int?
        var distance = 12.0
        var nearestDepth = Double.infinity
        for (index, track) in inspection.tracks.enumerated() {
            let p = track.position
            let projected = projectPoint(SCNVector3(p[0], p[1], p[2]))
            guard projected.z >= 0, projected.z <= 1 else { continue }
            let dy = (isFlipped ? bounds.height - projected.y : projected.y) - location.y
            let candidate = hypot(projected.x - location.x, dy)
            if candidate < distance || (abs(candidate - distance) < 0.1 && projected.z < nearestDepth) {
                nearest = index
                distance = candidate
                nearestDepth = projected.z
            }
        }
        if let nearest { selection?(nearest) }
    }

    func highlight(track: SparseTrack?) {
        scene?.rootNode.childNode(withName: "selected-track", recursively: false)?.removeFromParentNode()
        guard let track, let inspection, let scene else { return }
        let p = SCNVector3(track.position[0], track.position[1], track.position[2])
        let node = SCNNode()
        node.name = "selected-track"
        var rays: [SCNVector3] = []
        for observation in track.observations {
            if let camera = inspection.cameras[observation.imageID] {
                rays += [p, SCNVector3(camera.centre[0], camera.centre[1], camera.centre[2])]
            }
        }
        let lines = SCNGeometry(
            sources: [SCNGeometrySource(vertices: rays)],
            elements: [
                SCNGeometryElement(indices: Array(0..<UInt32(rays.count)), primitiveType: .line)
            ])
        lines.firstMaterial?.diffuse.contents = NSColor.systemYellow
        lines.firstMaterial?.lightingModel = .constant
        node.addChildNode(SCNNode(geometry: lines))
        let dot = SCNGeometryElement(indices: [UInt32(0)], primitiveType: .point)
        dot.minimumPointScreenSpaceRadius = 8
        dot.maximumPointScreenSpaceRadius = 8
        let marker = SCNGeometry(sources: [SCNGeometrySource(vertices: [p])], elements: [dot])
        marker.firstMaterial?.diffuse.contents = NSColor.systemOrange
        marker.firstMaterial?.lightingModel = .constant
        marker.firstMaterial?.readsFromDepthBuffer = false
        node.addChildNode(SCNNode(geometry: marker))
        scene.rootNode.addChildNode(node)
    }
}
