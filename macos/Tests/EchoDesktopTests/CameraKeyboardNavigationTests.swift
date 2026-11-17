import AppKit
import SceneKit
import XCTest

@testable import EchoDesktop

final class CameraKeyboardNavigationTests: XCTestCase {
    @MainActor
    func testSparseCameraKeysFollowPhotoOrderAndUpdateSelectionForRepeatedKeys() throws {
        let photos = try photographs()
        let scene = ReconstructionScene(
            frame: "local", origin: nil, cameras: photos.reversed().map { camera($0.id) }, points: [],
            unplaced: [], warnings: [])
        let view = ArtifactSceneView(frame: .zero)
        view.inspection = SparseInspectionData(
            root: URL(fileURLWithPath: "/tmp"), scene: scene, photos: photos, featureFiles: [:])
        var selections: [SparseSelection?] = []
        view.selection = { selections.append($0) }
        view.highlight(.camera("z"))

        view.keyDown(with: try key(124))
        view.keyDown(with: try key(124))
        view.keyDown(with: try key(124))
        view.keyDown(with: try key(123))

        XCTAssertEqual(selections, [.camera("a"), .camera("m"), .camera("a")])
        view.keyDown(with: try key(53))
        XCTAssertNil(try XCTUnwrap(selections.last))
        XCTAssertFalse(view.moveCamera(with: try key(124)))
    }

    @MainActor
    func testCloudCameraKeysSkipMissingPosesAndRespectOtherKeyboardActions() throws {
        let photos = try photographs()
        let view = ArtifactSceneView(frame: .zero)
        view.cameraInspection = CameraInspectionData(
            root: URL(fileURLWithPath: "/tmp"), cameras: [camera("m"), camera("z"), camera("missing-photo")],
            photos: photos)
        XCTAssertEqual(view.cameraInspection?.cameraOrder, ["z", "m"])
        var selections: [SparseSelection?] = []
        view.selection = { selections.append($0) }
        XCTAssertFalse(view.moveCamera(with: try key(124)))
        view.highlight(.point(0))
        XCTAssertFalse(view.moveCamera(with: try key(124)))
        view.highlight(.camera("z"))
        XCTAssertFalse(view.moveCamera(with: try key(124, modifiers: .command)))
        XCTAssertFalse(view.moveCamera(with: try key(125)))
        XCTAssertTrue(view.moveCamera(with: try key(123)))
        XCTAssertTrue(selections.isEmpty)
        view.keyDown(with: try key(124))
        XCTAssertEqual(selections, [.camera("m")])
        view.highlight(.camera("removed"))
        XCTAssertFalse(view.moveCamera(with: try key(124)))
    }

    @MainActor
    func testMeshCameraComparisonKeepsGeometryAndNavigatesFromVirtualViewport() throws {
        let photos = try photographs()
        let poses = photos.enumerated().map { index, photo in
            CameraPose(
                imageId: photo.id, centre: [Double(index), 0, -2],
                rotation: [1, 0, 0, 0, 1, 0, 0, 0, 1], focalPixels: 50, source: "aligned")
        }
        let data = CameraInspectionData(root: URL(fileURLWithPath: "/tmp"), cameras: poses, photos: photos)
        let surface = Surface(vertices: [[-2, -2, 0], [2, -2, 0], [0, 2, 0]], faces: [[0, 1, 2]], sourceImages: [])
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: data.root, surface: surface)
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let geometry = try XCTUnwrap(artifact.geometry)
        let view = ArtifactSceneView(frame: NSRect(x: 0, y: 0, width: 800, height: 600))
        view.scene = scene
        view.cameraInspection = data
        let preview = SourceCameraSceneView(frame: NSRect(x: 0, y: 0, width: 200, height: 160))
        preview.cameraKey = view.handleCameraKey
        var selected: SparseSelection?
        view.selection = { selection in
            selected = selection
            if case .camera(let imageID) = selection, let frustum = data.frustums[imageID] {
                preview.show(scene: scene, frustum: frustum)
            }
        }
        view.highlight(.camera("z"))
        XCTAssertTrue(artifact.geometry === geometry, "Camera selection must preserve mesh materials and topology")
        XCTAssertNil(artifact.childNode(withName: "cloud-context", recursively: false))
        XCTAssertNotNil(scene.rootNode.childNode(withName: "sparse-selection", recursively: false))

        preview.keyDown(with: try key(124))
        XCTAssertEqual(selected, .camera("a"))
        XCTAssertEqual(preview.pointOfView?.position.x, 1)
        XCTAssertTrue(
            preview.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.geometry === geometry)
        let previousScene = preview.scene
        preview.show(scene: scene, frustum: try XCTUnwrap(data.frustums["a"]))
        XCTAssertTrue(preview.scene === previousScene, "Unchanged camera updates must reuse the existing preview")
        preview.keyDown(with: try key(124))
        XCTAssertEqual(selected, .camera("m"))
        XCTAssertEqual(preview.pointOfView?.position.x, 2)
        preview.keyDown(with: try key(123))
        XCTAssertEqual(selected, .camera("a"))
        preview.keyDown(with: try key(53))
        XCTAssertNil(selected)
        XCTAssertNil(scene.rootNode.childNode(withName: "sparse-selection", recursively: false))
        XCTAssertTrue(artifact.geometry === geometry)
    }

    private func photographs() throws -> [Photo] {
        try ["z", "a", "m"].map { id in
            try JSONDecoder().decode(
                Photo.self,
                from: Data(
                    """
                    {"id":"\(id)","name":"\(id).jpg","source":"photo.jpg","preview":"preview.jpg",
                     "width":100,"height":80,"camera":"test","focalPixels":50,"enabled":true,"sets":[],"metadata":{}}
                    """.utf8))
        }
    }

    private func camera(_ id: String) -> CameraPose {
        CameraPose(
            imageId: id, centre: [0, 0, -2], rotation: [1, 0, 0, 0, 1, 0, 0, 0, 1], focalPixels: 50, source: "test")
    }

    private func key(_ code: UInt16, modifiers: NSEvent.ModifierFlags = []) throws -> NSEvent {
        try XCTUnwrap(
            NSEvent.keyEvent(
                with: .keyDown, location: .zero, modifierFlags: modifiers, timestamp: 0, windowNumber: 0,
                context: nil, characters: "", charactersIgnoringModifiers: "", isARepeat: false, keyCode: code))
    }
}
