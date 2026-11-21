import EchoRendering
import SceneKit
import XCTest
import simd

@testable import EchoDesktop

final class SelectedCameraFrustumTests: XCTestCase {
    @MainActor
    func testSelectedFrustumIsLargerAndAnchoredInSparseCloudAndSurfaceViews() throws {
        let photo = try photograph()
        let pose = camera(photo.id, centre: [10, 20, 30])
        let reconstruction = ReconstructionScene(
            frame: "local", origin: nil, cameras: [pose], points: [], unplaced: [], warnings: [])
        for stage in [PipelineStage.alignment, .cloud, .mesh, .texture] {
            let surface =
                stage == .mesh || stage == .texture
                ? Surface(
                    vertices: [[-10, -10, 0], [10, -10, 0], [0, 10, 0]], faces: [[0, 1, 2]], sourceImages: [])
                : nil
            let points = surface == nil ? [CloudPoint(position: [0, 0, 0], colour: nil, error: 0)] : []
            let scene = NativeSceneFactory().make(
                reconstruction: reconstruction, points: points, photos: [photo],
                root: URL(fileURLWithPath: "/tmp"), surface: surface)
            let ordinary = try XCTUnwrap(scene.rootNode.childNode(withName: photo.id, recursively: false))
            let ordinaryGeometry = ordinary.geometry
            let view = ArtifactSceneView(frame: .zero)
            view.scene = scene
            if stage == .alignment {
                view.inspection = SparseInspectionData(
                    root: URL(fileURLWithPath: "/tmp"), scene: reconstruction, photos: [photo], featureFiles: [:])
            } else {
                view.cameraInspection = CameraInspectionData(
                    root: URL(fileURLWithPath: "/tmp"), cameras: [pose], photos: [photo])
            }
            view.highlight(.camera(photo.id))
            let selected = try XCTUnwrap(
                scene.rootNode.childNode(withName: "selected-camera-frustum", recursively: true))
            let apex = SIMD3<Double>(selected.convertPosition(SCNVector3Zero, to: nil))
            let forward = SIMD3<Double>(selected.convertPosition(SCNVector3(0, 0, 1), to: nil)) - apex
            XCTAssertEqual(simd_distance(apex, SIMD3(10, 20, 30)), 0, accuracy: 1e-5)
            XCTAssertGreaterThanOrEqual(simd_length(forward), 4.8 - 1e-5)
            XCTAssertEqual(simd_normalize(forward).x, 1, accuracy: 1e-5)
            let frustum = try XCTUnwrap(CameraFrustum(camera: pose, photo: photo))
            let corner = SIMD3<Double>(selected.convertPosition(SCNVector3(1, 1, 1), to: nil)) - apex
            XCTAssertEqual(simd_dot(corner, frustum.right) / simd_length(forward), frustum.halfWidth, accuracy: 1e-5)
            XCTAssertEqual(simd_dot(corner, frustum.down) / simd_length(forward), frustum.halfHeight, accuracy: 1e-5)
            XCTAssertEqual(selected.geometry?.firstMaterial?.readsFromDepthBuffer, false)
            XCTAssertTrue(ordinary.geometry === ordinaryGeometry)
            XCTAssertEqual(ordinary.scale.x, 1)

            view.highlight(nil)
            XCTAssertNil(scene.rootNode.childNode(withName: "selected-camera-frustum", recursively: true))
            XCTAssertTrue(ordinary.geometry === ordinaryGeometry)
        }
    }

    @MainActor
    func testCameraOnlyScenesReuseFrustumGeometryWhenBrowsingAndRemoveItFromOldScenes() throws {
        let photo = try photograph()
        let nextPhoto = try photograph(id: "next")
        let poses = [camera(photo.id, centre: [10, 20, 30]), camera(nextPhoto.id, centre: [40, 50, 60])]
        let view = ArtifactSceneView(frame: .zero)
        view.scene = SCNScene()
        view.cameraInspection = CameraInspectionData(
            root: URL(fileURLWithPath: "/tmp"), cameras: poses, photos: [photo, nextPhoto])
        view.highlight(.camera(photo.id))
        let node = try XCTUnwrap(view.scene?.rootNode.childNode(withName: "selected-camera-frustum", recursively: true))
        let geometry = node.geometry
        view.highlight(.camera(nextPhoto.id))
        XCTAssertTrue(view.scene?.rootNode.childNode(withName: "selected-camera-frustum", recursively: true) === node)
        XCTAssertTrue(node.geometry === geometry)
        XCTAssertEqual(node.convertPosition(SCNVector3Zero, to: nil).x, 40, accuracy: 1e-5)
        let previousScene = view.scene
        view.scene = SCNScene()
        view.highlight(.camera(nextPhoto.id))
        XCTAssertNil(previousScene?.rootNode.childNode(withName: "selected-camera-frustum", recursively: true))
        XCTAssertNotNil(view.scene?.rootNode.childNode(withName: "selected-camera-frustum", recursively: true))
        view.highlight(nil)
        XCTAssertNil(view.scene?.rootNode.childNode(withName: "selected-camera-frustum", recursively: true))
    }

    private func photograph(id: String = "photo") throws -> Photo {
        try JSONDecoder().decode(
            Photo.self,
            from: Data(
                """
                {"id":"\(id)","name":"photo.jpg","source":"photo.jpg","preview":"preview.jpg",
                 "width":100,"height":80,"camera":"test","focalPixels":50,"enabled":true,"sets":[],"metadata":{}}
                """.utf8))
    }

    private func camera(_ id: String, centre: [Double]) -> CameraPose {
        CameraPose(
            imageId: id, centre: centre, rotation: [0, 0, 1, 0, 1, 0, -1, 0, 0], focalPixels: 50, source: "test")
    }
}
