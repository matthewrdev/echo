import SceneKit

final class ArtifactSceneView: SCNView {
    let renderObserver = SceneViewportRenderer()
    let exclusionOverlay = ExclusionSceneOverlay()
    let cropHandles = CropHandleInteraction()
    private var brushTracking: NSTrackingArea?
    var placeExclusion: ((SCNVector3) -> Void)?
    var artifactIdentity = ""
    var needsInitialFit = true
    var inspection: SparseInspectionData?
    var cameraInspection: CameraInspectionData?
    var selection: ((SparseSelection?) -> Void)?
    private let cloudHighlight = CloudCameraHighlight()
    private let optionCamera = OptionCameraInteraction()
    private var pointerInteraction = ScenePointerInteraction.idle
    private var pointerLocation = NSPoint.zero
    private var optionHeld = false
    private var mouseStart: NSPoint?
    private var highlighted: SparseSelection?
    private weak var highlightedScene: SCNScene?

    override func layout() {
        super.layout()
        if needsInitialFit, bounds.width > 1, bounds.height > 1, scene != nil {
            fitArtifact()
        }
    }

    func fitArtifact(direction: SceneViewDirection = .north) {
        guard let scene, let camera = pointOfView else { return }
        guard let artifact = scene.rootNode.childNode(withName: "artifact", recursively: false) else { return }
        let box = artifact.boundingBox
        let fit = SceneBounds(positions: [
            [box.min.x, box.min.y, box.min.z], [box.max.x, box.max.y, box.max.z],
        ])
        defaultCameraController.stopInertia()
        fit.fit(camera: camera, size: bounds.size, direction: direction)
        defaultCameraController.target = fit.centre
        needsInitialFit = false
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        let location = convert(event.locationInWindow, from: nil)
        updateModifiers(event.modifierFlags)
        pointerLocation = location
        mouseStart = nil
        if optionHeld {
            pointerInteraction = .optionCamera
            optionCamera.begin(at: location, modifiers: event.modifierFlags, in: self)
            return
        }
        if cropHandles.begin(at: location, in: self) {
            pointerInteraction = .tool
            return
        }
        pointerInteraction = .nativeCamera
        mouseStart = location
        super.mouseDown(with: event)
    }

    override func mouseDragged(with event: NSEvent) {
        updateModifiers(event.modifierFlags)
        pointerLocation = convert(event.locationInWindow, from: nil)
        switch pointerInteraction {
        case .tool:
            _ = cropHandles.move(to: pointerLocation, in: self)
        case .optionCamera:
            optionCamera.drag(to: pointerLocation, in: self)
        case .nativeCamera:
            super.mouseDragged(with: event)
        case .idle, .finished:
            break
        }
    }

    override func flagsChanged(with event: NSEvent) {
        updateModifiers(event.modifierFlags)
        super.flagsChanged(with: event)
    }

    private func updateModifiers(_ modifiers: NSEvent.ModifierFlags) {
        let held = modifiers.contains(.option)
        guard held != optionHeld else { return }
        optionHeld = held
        if held {
            mouseStart = nil
            if pointerInteraction == .tool || pointerInteraction == .finished {
                if pointerInteraction == .tool { finishTool(at: pointerLocation) }
                pointerInteraction = .optionCamera
                optionCamera.begin(at: pointerLocation, modifiers: modifiers, in: self)
            }
        } else if pointerInteraction == .optionCamera {
            optionCamera.end(at: pointerLocation, in: self)
            // A new click starts painting, so releasing Option during a drag never leaves an accidental mark.
            pointerInteraction = .finished
        }
        updatePointerCursor()
    }

    private func finishTool(at location: NSPoint) {
        _ = cropHandles.end(at: location, in: self)
    }

    private func updatePointerCursor() {
        guard bounds.contains(pointerLocation) else { return }
        if optionHeld {
            toolTip = nil
            if pointerInteraction == .optionCamera { NSCursor.closedHand.set() } else { NSCursor.openHand.set() }
        } else {
            cropHandles.hover(at: pointerLocation, in: self)
        }
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let brushTracking { removeTrackingArea(brushTracking) }
        let tracking = NSTrackingArea(
            rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self
        )
        addTrackingArea(tracking)
        brushTracking = tracking
    }

    override func mouseMoved(with event: NSEvent) {
        updateModifiers(event.modifierFlags)
        pointerLocation = convert(event.locationInWindow, from: nil)
        updatePointerCursor()
        super.mouseMoved(with: event)
    }

    override func mouseEntered(with event: NSEvent) {
        updateModifiers(event.modifierFlags)
        pointerLocation = convert(event.locationInWindow, from: nil)
        updatePointerCursor()
        super.mouseEntered(with: event)
    }

    override func mouseExited(with event: NSEvent) {
        pointerLocation = convert(event.locationInWindow, from: nil)
        toolTip = nil
        super.mouseExited(with: event)
    }

    override func mouseUp(with event: NSEvent) {
        updateModifiers(event.modifierFlags)
        let location = convert(event.locationInWindow, from: nil)
        pointerLocation = location
        let interaction = pointerInteraction
        pointerInteraction = .idle
        defer {
            mouseStart = nil
            updatePointerCursor()
        }
        switch interaction {
        case .tool:
            finishTool(at: location)
            return
        case .optionCamera:
            optionCamera.end(at: location, in: self)
            return
        case .idle, .finished:
            return
        case .nativeCamera:
            break
        }
        super.mouseUp(with: event)
        guard let start = mouseStart, hypot(location.x - start.x, location.y - start.y) < 4
        else { return }
        if let placeExclusion {
            if let point = exclusionPoint(at: location) {
                placeExclusion(point)
            }
            return
        }
        guard selection != nil else { return }
        if let cameraInspection {
            selection?(pick(at: location, cameras: cameraInspection.cameras, tracks: []))
        } else if let inspection {
            selection?(pick(at: location, inspection: inspection))
        }
    }

    func exclusionPoint(at location: NSPoint) -> SCNVector3? {
        guard let artifact = scene?.rootNode.childNode(withName: "artifact", recursively: false) else { return nil }
        if artifact.geometry?.elements.first?.primitiveType == .point {
            return CloudPointPicker().pick(at: location, in: self, node: artifact)
        }
        return hitTest(location, options: [.rootNode: artifact, .backFaceCulling: false]).first?.worldCoordinates
    }

    override func keyDown(with event: NSEvent) {
        if !handleCameraKey(event) { super.keyDown(with: event) }
    }

    func handleCameraKey(_ event: NSEvent) -> Bool {
        if event.keyCode == 53, cropHandles.cancel(in: self) { return true }
        if event.keyCode == 53, highlighted != nil {
            selection?(nil)
            highlight(nil)
            return true
        }
        return moveCamera(with: event)
    }

    func moveCamera(with event: NSEvent) -> Bool {
        guard event.modifierFlags.intersection([.command, .control, .option, .shift]).isEmpty,
            event.keyCode == 123 || event.keyCode == 124
        else { return false }
        return moveCamera(by: event.keyCode == 123 ? -1 : 1)
    }

    @discardableResult
    func moveCamera(by offset: Int) -> Bool {
        guard case .camera(let imageID) = highlighted, let selection else { return false }
        let cameras = cameraInspection?.cameraOrder ?? inspection?.cameraOrder ?? []
        guard let index = cameras.firstIndex(of: imageID) else { return false }
        let nextIndex = min(max(0, index + offset), cameras.count - 1)
        if nextIndex != index {
            let next = SparseSelection.camera(cameras[nextIndex])
            selection(next)
            highlight(next)
        }
        return true
    }

    func pick(at location: NSPoint, inspection: SparseInspectionData) -> SparseSelection? {
        pick(
            at: location, cameras: inspection.cameras, tracks: inspection.tracks,
            visibleTrackIndices: inspection.visibleTrackIndices)
    }

    func pick(
        at location: NSPoint, cameras: [String: CameraPose], tracks: [SparseTrack], visibleTrackIndices: Set<Int>? = nil
    ) -> SparseSelection? {
        var nearest: SparseSelection?
        var distance = 12.0
        var nearestDepth = Double.infinity
        for camera in cameras.values.sorted(by: { $0.imageId < $1.imageId }) {
            let p = camera.centre
            let projected = projectPoint(SCNVector3(p[0], p[1], p[2]))
            guard projected.z >= 0, projected.z <= 1 else { continue }
            let dy = (isFlipped ? bounds.height - projected.y : projected.y) - location.y
            let candidate = hypot(projected.x - location.x, dy)
            if candidate < distance || (abs(candidate - distance) < 0.1 && projected.z < nearestDepth) {
                nearest = .camera(camera.imageId)
                distance = candidate
                nearestDepth = projected.z
            }
        }
        for (index, track) in tracks.enumerated() where visibleTrackIndices?.contains(index) ?? true {
            let p = track.position
            let projected = projectPoint(SCNVector3(p[0], p[1], p[2]))
            guard projected.z >= 0, projected.z <= 1 else { continue }
            let dy = (isFlipped ? bounds.height - projected.y : projected.y) - location.y
            let candidate = hypot(projected.x - location.x, dy)
            if candidate < distance || (abs(candidate - distance) < 0.1 && projected.z < nearestDepth) {
                nearest = .point(index)
                distance = candidate
                nearestDepth = projected.z
            }
        }
        return nearest
    }

    func highlight(_ selected: SparseSelection?) {
        guard highlighted != selected || highlightedScene !== scene else { return }
        highlighted = selected
        highlightedScene = scene
        scene?.rootNode.childNode(withName: "sparse-selection", recursively: false)?.removeFromParentNode()
        scene?.rootNode.childNode(withName: "artifact", recursively: false)?.opacity = 1
        if case .camera(let imageID) = selected,
            let frustum = cameraInspection?.frustums[imageID] ?? inspection?.frustums[imageID], let scene,
            let artifact = scene.rootNode.childNode(withName: "artifact", recursively: false)
        {
            if artifact.geometry?.elements.first?.primitiveType == .point {
                cloudHighlight.show(frustum, on: artifact)
            } else {
                cloudHighlight.clear()
            }
            let marker = markers(
                [SCNVector3(frustum.centre.x, frustum.centre.y, frustum.centre.z)], color: .systemYellow, radius: 8)
            marker.name = "sparse-selection"
            scene.rootNode.addChildNode(marker)
            return
        }
        cloudHighlight.clear()
        guard let selected, let inspection, let scene else { return }
        switch selected {
        case .point(let index):
            guard inspection.tracks.indices.contains(index) else { return }
            highlight(track: inspection.tracks[index], inspection: inspection, scene: scene)
        case .camera(let imageID):
            guard let camera = inspection.cameras[imageID] else { return }
            let node = SCNNode()
            node.name = "sparse-selection"
            let positions = (inspection.cameraTracks[imageID] ?? []).map { index in
                let p = inspection.tracks[index].position
                return SCNVector3(p[0], p[1], p[2])
            }
            node.addChildNode(markers(positions, color: .systemYellow, radius: 4))
            node.addChildNode(
                markers(
                    [SCNVector3(camera.centre[0], camera.centre[1], camera.centre[2])], color: .systemOrange, radius: 8)
            )
            scene.rootNode.childNode(withName: "artifact", recursively: false)?.opacity = 0.15
            scene.rootNode.addChildNode(node)
        }
    }

    private func highlight(track: SparseTrack, inspection: SparseInspectionData, scene: SCNScene) {
        let p = SCNVector3(track.position[0], track.position[1], track.position[2])
        let node = SCNNode()
        node.name = "sparse-selection"
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
        node.addChildNode(markers([p], color: .systemOrange, radius: 8))
        scene.rootNode.addChildNode(node)
    }

    private func markers(_ positions: [SCNVector3], color: NSColor, radius: CGFloat) -> SCNNode {
        let dots = SCNGeometryElement(
            data: nil, primitiveType: .point, primitiveCount: positions.count, bytesPerIndex: 0)
        dots.minimumPointScreenSpaceRadius = radius
        dots.maximumPointScreenSpaceRadius = radius
        let geometry = SCNGeometry(sources: [SCNGeometrySource(vertices: positions)], elements: [dots])
        geometry.firstMaterial?.diffuse.contents = color
        geometry.firstMaterial?.lightingModel = .constant
        geometry.firstMaterial?.readsFromDepthBuffer = false
        let node = SCNNode(geometry: geometry)
        node.renderingOrder = 10
        return node
    }
}
