import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class SceneInteractionTests: XCTestCase {
    @MainActor
    func testRenderedCameraPublishesChangedAxesWithoutMovingScene() async throws {
        let device = try XCTUnwrap(MTLCreateSystemDefaultDevice())
        let scene = SCNScene()
        let camera = SCNNode()
        camera.camera = SCNCamera()
        camera.position.z = 10
        scene.rootNode.addChildNode(camera)
        let orientation = SceneOrientationState()
        let observer = SceneViewportRenderer()
        observer.orientation = orientation
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        renderer.delegate = observer
        _ = renderer.snapshot(atTime: 0, with: CGSize(width: 100, height: 100), antialiasingMode: .none)
        await Task.yield()
        let first = try XCTUnwrap(orientation.frame)
        XCTAssertEqual(first.east.x, 1, accuracy: 0.001)
        camera.position = SCNVector3(10, 10, 0)
        camera.look(at: SCNVector3Zero)
        _ = renderer.snapshot(atTime: 1, with: CGSize(width: 100, height: 100), antialiasingMode: .none)
        await Task.yield()
        let rotated = try XCTUnwrap(orientation.frame)
        XCTAssertNotEqual(first, rotated)
        XCTAssertEqual(abs(rotated.north.x), 1, accuracy: 0.001)
        XCTAssertLessThan(rotated.up.y, first.up.y)
        XCTAssertGreaterThan(rotated.up.y, 0)
    }

    @MainActor
    func testDefaultViewSelectsCameraAndPointAndClearsHighlight() throws {
        let scene = SCNScene()
        let camera = SCNNode()
        camera.camera = SCNCamera()
        camera.position.z = 10
        scene.rootNode.addChildNode(camera)
        let artifact = SCNNode()
        artifact.name = "artifact"
        scene.rootNode.addChildNode(artifact)
        let pose = CameraPose(
            imageId: "photo", centre: [-2, 1, 0], rotation: [1, 0, 0, 0, 1, 0, 0, 0, 1], focalPixels: 1000,
            source: "test")
        let point = CloudPoint(
            position: [2, -1, 0], colour: nil, error: 0, imageA: "photo", imageB: "other", pixelA: [10, 20],
            pixelB: [20, 30])
        let reconstruction = ReconstructionScene(
            frame: "local", origin: nil, cameras: [pose], points: [point], unplaced: [], warnings: [])
        let data = SparseInspectionData(
            root: URL(fileURLWithPath: "/tmp"), scene: reconstruction, photos: [], featureFiles: [:])
        let view = ArtifactSceneView(frame: NSRect(x: 0, y: 0, width: 800, height: 600))
        view.scene = scene
        view.pointOfView = camera
        view.inspection = data
        _ = view.snapshot()
        for position in [SCNVector3(-2, 1, 0), SCNVector3(2, -1, 0)] {
            let screen = view.projectPoint(position)
            let location = NSPoint(x: screen.x, y: view.isFlipped ? view.bounds.height - screen.y : screen.y)
            XCTAssertEqual(view.pick(at: location, inspection: data), position.x < 0 ? .camera("photo") : .point(0))
        }
        view.highlight(.camera("photo"))
        XCTAssertEqual(artifact.opacity, 0.15, accuracy: 0.000001)
        let selection = try XCTUnwrap(scene.rootNode.childNode(withName: "sparse-selection", recursively: false))
        XCTAssertEqual(selection.childNodes.first?.geometry?.sources(for: .vertex).first?.vectorCount, 1)
        view.highlight(.point(0))
        XCTAssertEqual(artifact.opacity, 1)
        view.highlight(nil)
        XCTAssertNil(scene.rootNode.childNode(withName: "sparse-selection", recursively: false))
        XCTAssertNil(view.pick(at: NSPoint(x: 0, y: 0), inspection: data))
        let hidden = SparseInspectionData(
            root: URL(fileURLWithPath: "/tmp"), scene: reconstruction, photos: [], featureFiles: [:], visiblePoints: [])
        let screen = view.projectPoint(SCNVector3(2, -1, 0))
        let location = NSPoint(x: screen.x, y: view.isFlipped ? view.bounds.height - screen.y : screen.y)
        XCTAssertNil(view.pick(at: location, inspection: hidden), "Filtered-out points must not receive clicks")
    }
}
