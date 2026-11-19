import AppKit
import SceneKit
import XCTest
import simd

@testable import EchoDesktop

final class CloudOrbitTests: XCTestCase {
    @MainActor
    func testCloudOrbitPicksFrontPointIgnoresOverlaysAndKeepsItFixedOnScreen() throws {
        let view = makeView()
        let orbit = ArtifactOrbitInteraction()
        let camera = try XCTUnwrap(view.pointOfView)
        let point = SCNVector3(0.5, 0.25, 1)
        let projected = view.projectPoint(point)
        let screen = NSPoint(x: projected.x, y: projected.y)
        let original = camera.simdWorldTransform
        let radius = simd_length(camera.simdWorldPosition - SIMD3(0.5, 0.25, 1))
        XCTAssertTrue(orbit.begin(at: screen, in: view))
        XCTAssertEqual(camera.simdWorldTransform, original, "Picking a pivot must not re-aim the camera")
        let pivot = view.defaultCameraController.target
        XCTAssertEqual(pivot.x, point.x, accuracy: 0.0001)
        XCTAssertEqual(pivot.y, point.y, accuracy: 0.0001)
        XCTAssertEqual(pivot.z, point.z, accuracy: 0.0001, "The rear cloud point and overlay are not the surface")
        orbit.drag(to: NSPoint(x: screen.x + 60, y: screen.y + 20), in: view)
        let moved = view.projectPoint(point)
        XCTAssertEqual(moved.x, projected.x, accuracy: 0.001)
        XCTAssertEqual(moved.y, projected.y, accuracy: 0.001)
        XCTAssertEqual(simd_length(camera.simdWorldPosition - SIMD3(0.5, 0.25, 1)), radius, accuracy: 0.0001)
        orbit.end(in: view)
        XCTAssertTrue(view.allowsCameraControl)
        XCTAssertTrue(orbit.begin(at: NSPoint(x: -500, y: -500), in: view))
        XCTAssertEqual(view.defaultCameraController.target.x, pivot.x)
        XCTAssertEqual(view.defaultCameraController.target.y, pivot.y)
        XCTAssertEqual(view.defaultCameraController.target.z, pivot.z)
        orbit.end(in: view)
    }

    @MainActor
    func testOrdinaryAndOptionDragUseCloudPivotAndPlainClicksStillSelect() throws {
        for modifiers in [NSEvent.ModifierFlags(), .option] {
            let view = makeView()
            var selections = 0
            view.selectSurfacePoint = { _ in selections += 1 }
            let point = view.projectPoint(SCNVector3(0.5, 0.25, 1))
            func event(_ type: NSEvent.EventType, dx: Double = 0) throws -> NSEvent {
                try XCTUnwrap(
                    NSEvent.mouseEvent(
                        with: type, location: NSPoint(x: point.x + dx, y: point.y), modifierFlags: modifiers,
                        timestamp: 0, windowNumber: 0, context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
            }
            view.mouseDown(with: try event(.leftMouseDown))
            XCTAssertEqual(view.defaultCameraController.target.z, 1, accuracy: 0.0001)
            view.mouseUp(with: try event(.leftMouseUp))
            XCTAssertEqual(selections, modifiers.isEmpty ? 1 : 0)
            view.mouseDown(with: try event(.leftMouseDown))
            view.mouseDragged(with: try event(.leftMouseDragged, dx: 40))
            view.mouseUp(with: try event(.leftMouseUp, dx: 40))
            XCTAssertEqual(selections, modifiers.isEmpty ? 1 : 0, "Dragging must not also select")
            XCTAssertTrue(view.allowsCameraControl)
        }
    }

    @MainActor
    private func makeView() -> ArtifactSceneView {
        let scene = NativeSceneFactory().make(
            reconstruction: nil,
            points: [
                CloudPoint(position: [0.4, 0.05, 1], colour: nil, error: 0),
                CloudPoint(position: [0.5, 0.1, -0.4], colour: nil, error: 0),
                CloudPoint(position: [-2, -2, 0], colour: nil, error: 0),
                CloudPoint(position: [2, 2, 0], colour: nil, error: 0),
            ], photos: [], root: URL(fileURLWithPath: "/tmp"))
        scene.rootNode.childNode(withName: "artifact", recursively: false)?.position = SCNVector3(0.1, 0.2, 0)
        let overlay = SCNNode(geometry: SCNPlane(width: 10, height: 10))
        overlay.name = "selected-camera-frustum"
        overlay.position.z = 2
        scene.rootNode.addChildNode(overlay)
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 800, height: 600))
        view.scene = scene
        view.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.pointOfView?.position = SCNVector3(0, 0, 8)
        view.pointOfView?.look(at: SCNVector3Zero)
        view.pointOfView?.camera?.zNear = 0.01
        view.pointOfView?.camera?.zFar = 100
        view.needsInitialFit = false
        view.allowsCameraControl = true
        return view
    }
}
