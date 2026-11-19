import SceneKit
import XCTest

@testable import EchoDesktop

final class PhotographedViewTests: XCTestCase {
    func testPhotographedSideRejectsOutliersAndHandlesVerticalOrMissingCameras() {
        let bounds = SceneBounds(positions: [[-1, -1, -1], [1, 1, 1]])
        let view = PhotographedView(
            bounds: bounds, cameras: [camera([-10, 0, 0]), camera([-12, 0, 0]), camera([1000, 0, 0])])
        XCTAssertEqual(view.offset.x, -1, accuracy: 0.000001)
        XCTAssertEqual(view.offset.z, 0, accuracy: 0.000001)
        let vertical = PhotographedView(bounds: bounds, cameras: [camera([0, 10, 0])])
        XCTAssertEqual(vertical.offset.y, 1, accuracy: 0.000001)
        XCTAssertEqual(vertical.up.z, -1)
        for cameras in [[], [camera([0, 0, 0])], [camera([.nan, 0, 0])]] {
            let fallback = PhotographedView(bounds: bounds, cameras: cameras)
            XCTAssertEqual(fallback.offset.z, 1)
        }
    }

    @MainActor
    func testInitialMeshViewAndFitFacePhotographsWhileCompassViewsRemainExplicit() throws {
        let reconstruction = ReconstructionScene(
            frame: "local", origin: nil, cameras: [camera([-10, 0, 0])], points: [], unplaced: [], warnings: [])
        let surface = Surface(
            vertices: [[-1, -1, -1], [1, -1, -1], [0, 1, 1]], faces: [[0, 1, 2]], sourceImages: [])
        let scene = NativeSceneFactory().make(
            reconstruction: reconstruction, points: [], photos: [], root: URL(fileURLWithPath: "/tmp"), surface: surface
        )
        let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        XCTAssertLessThan(camera.position.x, -1)
        let view = ArtifactSceneView(frame: NSRect(x: 0, y: 0, width: 300, height: 900))
        view.scene = scene
        view.pointOfView = camera
        view.fitArtifact()
        XCTAssertLessThan(camera.position.x, -1)
        XCTAssertEqual(camera.position.z, 0, accuracy: 0.00001)
        view.fitArtifact(direction: .north)
        XCTAssertGreaterThan(camera.position.z, 1)
        XCTAssertEqual(camera.position.x, 0, accuracy: 0.000001)
    }

    private func camera(_ centre: [Double]) -> CameraPose {
        CameraPose(
            imageId: "photo", centre: centre, rotation: [1, 0, 0, 0, 1, 0, 0, 0, 1], focalPixels: 100, source: "test")
    }
}
