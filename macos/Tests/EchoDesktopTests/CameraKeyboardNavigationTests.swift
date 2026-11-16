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
        view.cloudInspection = CloudInspectionData(
            root: URL(fileURLWithPath: "/tmp"), cameras: [camera("m"), camera("z"), camera("missing-photo")],
            photos: photos)
        XCTAssertEqual(view.cloudInspection?.cameraOrder, ["z", "m"])
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
