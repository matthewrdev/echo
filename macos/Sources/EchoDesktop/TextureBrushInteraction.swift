import SceneKit
import simd

@MainActor
final class TextureBrushInteraction {
    var configuration: TextureBrushConfiguration?
    var painting: TexturePriorityPainting?
    var showHeatmap = true
    var finished: ((TexturePriorityStroke) -> Void)?
    private let overlay = TexturePriorityOverlay()
    private var stroke: TexturePriorityStroke?
    private var lastLocation: NSPoint?
    private var cameraControlEnabled = false
    private let cursor = CAShapeLayer()

    func refresh(in view: ArtifactSceneView) {
        var strokes = painting?.strokes ?? []
        if let stroke { strokes.append(stroke) }
        overlay.update(scene: view.scene, strokes: showHeatmap ? strokes : [])
        if configuration == nil { hideCursor() }
    }

    func begin(at location: NSPoint, in view: ArtifactSceneView) -> Bool {
        guard let configuration else { return false }
        view.defaultCameraController.stopInertia()
        cameraControlEnabled = view.allowsCameraControl
        view.allowsCameraControl = false
        stroke = TexturePriorityStroke(weight: configuration.weight)
        lastLocation = nil
        stamp(at: location, in: view)
        return true
    }

    func drag(to location: NSPoint, in view: ArtifactSceneView) -> Bool {
        guard stroke != nil else { return false }
        stamp(at: location, in: view)
        return true
    }

    func end(in view: ArtifactSceneView) -> Bool {
        guard let completed = stroke else { return false }
        stroke = nil
        lastLocation = nil
        view.allowsCameraControl = cameraControlEnabled
        if !completed.samples.isEmpty {
            painting?.strokes.append(completed)
            finished?(completed)
        }
        refresh(in: view)
        return true
    }

    @discardableResult
    func cancel(in view: ArtifactSceneView) -> Bool {
        let active = stroke != nil
        stroke = nil
        lastLocation = nil
        if active { view.allowsCameraControl = cameraControlEnabled }
        hideCursor()
        refresh(in: view)
        return active
    }

    func moveCursor(to location: NSPoint, in view: ArtifactSceneView) {
        guard let configuration else { return }
        if cursor.superlayer !== view.layer {
            cursor.removeFromSuperlayer()
            view.layer?.addSublayer(cursor)
        }
        let colour: NSColor = configuration.weight > 1 ? .systemOrange : configuration.weight < 1 ? .systemBlue : .white
        let radius = configuration.radiusPixels
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        cursor.frame = view.bounds
        cursor.path = CGPath(
            ellipseIn: CGRect(x: location.x - radius, y: location.y - radius, width: radius * 2, height: radius * 2),
            transform: nil)
        cursor.strokeColor = colour.cgColor
        cursor.fillColor = colour.withAlphaComponent(0.08).cgColor
        cursor.lineWidth = 1.5
        cursor.isHidden = false
        CATransaction.commit()
    }

    func hideCursor() {
        cursor.isHidden = true
    }

    func sample(at location: NSPoint, in view: ArtifactSceneView) -> TextureBrushSample? {
        guard let configuration,
            let artifact = view.scene?.rootNode.childNode(withName: "artifact", recursively: false)
        else { return nil }
        let point: SCNVector3?
        if artifact.geometry?.elements.first?.primitiveType == .point {
            point = CloudPointPicker().pick(
                at: location, in: view, node: artifact, pixelRadius: 6, preferFrontmost: true)
        } else {
            point =
                view.hitTest(location, options: [.rootNode: artifact, .backFaceCulling: false]).first?.worldCoordinates
        }
        guard let point else { return nil }
        let projected = view.projectPoint(point)
        let edge = view.unprojectPoint(SCNVector3(projected.x + configuration.radiusPixels, projected.y, projected.z))
        let near = view.unprojectPoint(SCNVector3(projected.x, projected.y, 0))
        let far = view.unprojectPoint(SCNVector3(projected.x, projected.y, 1))
        let normal = simd_normalize(SIMD3(near.x - far.x, near.y - far.y, near.z - far.z))
        let radius = simd_length(SIMD3(edge.x - point.x, edge.y - point.y, edge.z - point.z))
        let sample = TextureBrushSample(
            centre: [point.x, point.y, point.z], normal: [normal.x, normal.y, normal.z], radius: radius,
            depth: radius * 0.25)
        return sample.isValid ? sample : nil
    }

    private func stamp(at location: NSPoint, in view: ArtifactSceneView) {
        moveCursor(to: location, in: view)
        if let lastLocation, let configuration,
            hypot(location.x - lastLocation.x, location.y - lastLocation.y) < configuration.radiusPixels * 0.2
        {
            return
        }
        guard let sample = sample(at: location, in: view) else { return }
        stroke?.samples.append(sample)
        lastLocation = location
        refresh(in: view)
    }
}
