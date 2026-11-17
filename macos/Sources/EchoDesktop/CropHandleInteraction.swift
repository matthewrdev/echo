import SceneKit

@MainActor
final class CropHandleInteraction {
    private let overlay = CropHandleOverlay()
    private weak var scene: SCNScene?
    private var regions: [ExclusionRegion] = []
    private var selectedID: String?
    private var changed: ((ExclusionRegion) -> Void)?
    private var drag: CropHandleDrag?
    private var ownsGesture = false
    private var cameraControlEnabled = false

    func update(
        in view: ArtifactSceneView, regions: [ExclusionRegion], selectedID: String?,
        changed: ((ExclusionRegion) -> Void)?
    ) {
        let selected = regions.first { $0.id == selectedID }
        let available = changed != nil && selected?.enabled == true && selected?.isValid == true
        let targetChanged = scene !== view.scene || self.selectedID != selectedID || !available
        if targetChanged || selected != self.regions.first(where: { $0.id == self.selectedID }) {
            drag = nil
        }
        scene = view.scene
        self.regions = regions
        self.selectedID = selectedID
        self.changed = changed
        overlay.update(scene: view.scene, region: available ? selected : nil)
    }

    func begin(at location: NSPoint, in view: ArtifactSceneView) -> Bool {
        guard changed != nil, let region = regions.first(where: { $0.id == selectedID }),
            let handle = pick(at: location, in: view)
        else { return false }
        view.defaultCameraController.stopInertia()
        cameraControlEnabled = view.allowsCameraControl
        view.allowsCameraControl = false
        drag = CropHandleDrag(region: region, handle: handle, start: location, view: view)
        ownsGesture = true
        NSCursor.closedHand.set()
        return true
    }

    func move(to location: NSPoint, in view: ArtifactSceneView) -> Bool {
        guard ownsGesture else { return false }
        if let region = drag?.region(at: location, view: view) { apply(region, in: view) }
        return true
    }

    func end(at location: NSPoint, in view: ArtifactSceneView) -> Bool {
        guard ownsGesture else { return false }
        _ = move(to: location, in: view)
        drag = nil
        ownsGesture = false
        view.allowsCameraControl = cameraControlEnabled
        hover(at: location, in: view)
        return true
    }

    func cancel(in view: ArtifactSceneView) -> Bool {
        guard ownsGesture else { return false }
        if let original = drag?.original { apply(original, in: view) }
        drag = nil
        NSCursor.arrow.set()
        // Consume the remaining events, so releasing after Escape never picks an artifact or moves the camera.
        return true
    }

    func hover(at location: NSPoint, in view: ArtifactSceneView) {
        guard !ownsGesture else { return }
        let handle = pick(at: location, in: view)
        view.toolTip = handle?.kind.help
        if handle == nil { NSCursor.arrow.set() } else { NSCursor.openHand.set() }
    }

    func pick(at location: NSPoint, in view: SCNView) -> CropHandle? {
        guard changed != nil, let region = regions.first(where: { $0.id == selectedID }) else { return nil }
        var nearest: CropHandle?
        var distance = 12.0
        for handle in CropHandle.handles(for: region) {
            let point = view.projectPoint(handle.point)
            guard point.z >= 0, point.z <= 1 else { continue }
            let y = view.isFlipped ? view.bounds.height - point.y : point.y
            let candidate = hypot(point.x - location.x, y - location.y)
            // The centre wins when a height/depth handle projects onto it in an axis-aligned view.
            if candidate < distance - 0.5 {
                nearest = handle
                distance = candidate
            }
        }
        return nearest
    }

    private func apply(_ region: ExclusionRegion, in view: ArtifactSceneView) {
        guard let index = regions.firstIndex(where: { $0.id == region.id }) else { return }
        regions[index] = region
        view.exclusionOverlay.update(scene: view.scene, regions: regions, selectedID: selectedID)
        overlay.update(scene: view.scene, region: region)
        changed?(region)
    }
}
