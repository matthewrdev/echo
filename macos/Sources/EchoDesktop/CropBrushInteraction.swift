import SceneKit

@MainActor
final class CropBrushInteraction {
    var configuration: CropBrushConfiguration?
    var finished: ((ExclusionRegion) -> Void)?
    var failed: ((String) -> Void)?
    private var hull: [CGPoint] = []
    private var activeConfiguration: CropBrushConfiguration?
    private var cameraControlEnabled = false
    private let outline = CAShapeLayer()
    private let cursor = CAShapeLayer()

    var active: Bool { activeConfiguration != nil }

    func begin(at location: NSPoint, in view: ArtifactSceneView) -> Bool {
        guard let configuration else { return false }
        activeConfiguration = configuration
        cameraControlEnabled = view.allowsCameraControl
        view.defaultCameraController.stopInertia()
        view.allowsCameraControl = false
        hull = []
        _ = drag(to: location, in: view)
        return true
    }

    func drag(to location: NSPoint, in view: ArtifactSceneView) -> Bool {
        guard let configuration = activeConfiguration else { return false }
        hull = ScreenCropHull().adding(location, radius: configuration.radius, to: hull)
        attach(outline, to: view)
        let path = CGMutablePath()
        if let first = hull.first {
            path.move(to: first)
            for point in hull.dropFirst() { path.addLine(to: point) }
            path.closeSubpath()
        }
        let colour = configuration.mode == .include ? NSColor.systemGreen : NSColor.systemRed
        outline.path = path
        outline.fillColor = colour.withAlphaComponent(0.2).cgColor
        outline.strokeColor = colour.cgColor
        outline.lineWidth = 2
        moveCursor(to: location, in: view)
        return true
    }

    func end(in view: ArtifactSceneView) -> Bool {
        guard let configuration = activeConfiguration else { return false }
        let points = hull
        _ = cancel(in: view)
        do {
            finished?(try ProjectedCropHullBuilder().make(points: points, in: view, mode: configuration.mode))
        } catch {
            failed?(error.localizedDescription)
        }
        return true
    }

    @discardableResult
    func cancel(in view: ArtifactSceneView) -> Bool {
        let wasActive = active
        if wasActive { view.allowsCameraControl = cameraControlEnabled }
        activeConfiguration = nil
        hull = []
        outline.removeFromSuperlayer()
        hideCursor()
        return wasActive
    }

    func moveCursor(to location: NSPoint, in view: ArtifactSceneView) {
        guard let configuration else { return }
        attach(cursor, to: view)
        let radius = configuration.radius
        cursor.path = CGPath(
            ellipseIn: CGRect(
                x: location.x - radius, y: location.y - radius,
                width: radius * 2, height: radius * 2), transform: nil)
        cursor.strokeColor = (configuration.mode == .include ? NSColor.systemGreen : NSColor.systemRed).cgColor
        cursor.fillColor = NSColor.clear.cgColor
        cursor.lineWidth = 1.5
        NSCursor.crosshair.set()
    }

    func hideCursor() { cursor.removeFromSuperlayer() }

    private func attach(_ layer: CAShapeLayer, to view: ArtifactSceneView) {
        if layer.superlayer !== view.layer { view.layer?.addSublayer(layer) }
        layer.frame = view.bounds
        layer.actions = ["path": NSNull(), "bounds": NSNull(), "position": NSNull()]
    }
}
