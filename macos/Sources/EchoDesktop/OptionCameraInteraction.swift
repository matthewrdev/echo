import SceneKit

@MainActor
final class OptionCameraInteraction {
    private let meshOrbit = MeshOrbitInteraction()
    private var cameraControlEnabled = false
    private var previousMode: SCNInteractionMode = .orbitTurntable

    func begin(at location: NSPoint, modifiers: NSEvent.ModifierFlags, in view: SCNView) {
        if !modifiers.contains(.shift), meshOrbit.begin(at: location, in: view) { return }
        cameraControlEnabled = view.allowsCameraControl
        view.allowsCameraControl = false
        let controller = view.defaultCameraController
        controller.stopInertia()
        previousMode = controller.interactionMode
        controller.pointOfView = view.pointOfView
        controller.interactionMode = modifiers.contains(.shift) ? .pan : .orbitTurntable
        controller.beginInteraction(location, withViewport: view.bounds.size)
        NSCursor.closedHand.set()
    }

    func drag(to location: NSPoint, in view: SCNView) {
        if meshOrbit.active {
            meshOrbit.drag(to: location, in: view)
            return
        }
        view.defaultCameraController.continueInteraction(location, withViewport: view.bounds.size, sensitivity: 1)
    }

    func end(at location: NSPoint, in view: SCNView) {
        if meshOrbit.active {
            meshOrbit.end(in: view)
            return
        }
        let controller = view.defaultCameraController
        controller.endInteraction(location, withViewport: view.bounds.size, velocity: .zero)
        controller.stopInertia()
        controller.interactionMode = previousMode
        view.allowsCameraControl = cameraControlEnabled
    }
}
