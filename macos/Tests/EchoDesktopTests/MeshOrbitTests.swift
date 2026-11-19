import AppKit
import SceneKit
import XCTest
import simd

@testable import EchoDesktop

final class MeshOrbitTests: XCTestCase {
    @MainActor
    func testPickedSurfaceBecomesPivotWithoutJumpingAndBackgroundRetainsIt() throws {
        let view = makeView()
        let orbit = ArtifactOrbitInteraction()
        let point = SCNVector3(0.7, 0.3, 0)
        let projected = view.projectPoint(point)
        let screen = CGPoint(x: projected.x, y: projected.y)
        let camera = try XCTUnwrap(view.pointOfView)
        let transform = camera.simdWorldTransform
        XCTAssertTrue(orbit.begin(at: screen, in: view))
        XCTAssertEqual(camera.simdWorldTransform, transform)
        let pivot = view.defaultCameraController.target
        XCTAssertEqual(pivot.x, point.x, accuracy: 0.0001)
        XCTAssertEqual(pivot.y, point.y, accuracy: 0.0001)
        XCTAssertEqual(pivot.z, point.z, accuracy: 0.0001)
        let distance = simd_length(camera.simdWorldPosition - SIMD3(Float(pivot.x), Float(pivot.y), Float(pivot.z)))
        orbit.drag(to: CGPoint(x: screen.x + 30, y: screen.y + 12), in: view)
        let moved = view.projectPoint(pivot)
        XCTAssertEqual(moved.x, projected.x, accuracy: 0.001)
        XCTAssertEqual(moved.y, projected.y, accuracy: 0.001)
        XCTAssertEqual(
            simd_length(camera.simdWorldPosition - SIMD3(Float(pivot.x), Float(pivot.y), Float(pivot.z))), distance,
            accuracy: 0.0001)
        orbit.end(in: view)
        XCTAssertTrue(view.allowsCameraControl)
        XCTAssertTrue(orbit.begin(at: CGPoint(x: -500, y: -500), in: view))
        XCTAssertEqual(view.defaultCameraController.target.x, pivot.x)
        XCTAssertEqual(view.defaultCameraController.target.y, pivot.y)
        orbit.end(in: view)
    }

    @MainActor
    func testOrdinaryAndOptionGesturesBothUseSurfacePivotAndPreserveClicks() throws {
        for modifiers in [NSEvent.ModifierFlags(), .option] {
            let view = makeView()
            var selections = 0
            view.selectSurfacePoint = { _ in selections += 1 }
            let point = view.projectPoint(SCNVector3(-0.7, 0.2, 0))
            func event(_ type: NSEvent.EventType, dx: Double = 0) throws -> NSEvent {
                try XCTUnwrap(
                    NSEvent.mouseEvent(
                        with: type, location: CGPoint(x: point.x + dx, y: point.y),
                        modifierFlags: modifiers, timestamp: 0, windowNumber: 0, context: nil, eventNumber: 0,
                        clickCount: 1, pressure: 1))
            }
            view.mouseDown(with: try event(.leftMouseDown))
            XCTAssertEqual(view.defaultCameraController.target.x, -0.7, accuracy: 0.001)
            view.mouseDragged(with: try event(.leftMouseDragged, dx: 30))
            view.mouseUp(with: try event(.leftMouseUp, dx: 30))
            XCTAssertEqual(selections, 0)
            XCTAssertTrue(view.allowsCameraControl)
        }
    }

    @MainActor
    private func makeView() -> ArtifactSceneView {
        let surface = Surface(
            vertices: [[-2, -2, 0], [2, -2, 0], [2, 2, 0], [-2, 2, 0]], faces: [[0, 1, 2], [0, 2, 3]], sourceImages: [])
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 800, height: 600))
        view.scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: URL(fileURLWithPath: "/tmp"), surface: surface)
        view.pointOfView = view.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.fitArtifact(direction: .north)
        view.allowsCameraControl = true
        return view
    }
}
