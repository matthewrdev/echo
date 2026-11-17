import AppKit
import SceneKit
import XCTest

@testable import EchoDesktop

final class OptionCameraInteractionTests: XCTestCase {
    @MainActor
    func testOptionDragOrbitsWithoutPaintingAndReleaseRestoresBrush() throws {
        let view = makeView()
        var strokes: [TexturePriorityStroke] = []
        view.textureBrush.finished = { strokes.append($0) }
        let start = centre(in: view)
        let destination = NSPoint(x: start.x + 60, y: start.y + 30)
        let originalCamera = view.pointOfView?.simdTransform
        view.mouseMoved(with: try mouse(.mouseMoved, at: start))
        let cursor = try XCTUnwrap(view.layer?.sublayers?.compactMap { $0 as? CAShapeLayer }.first)
        XCTAssertFalse(cursor.isHidden)
        view.flagsChanged(with: try modifiers(.option))
        XCTAssertTrue(cursor.isHidden)
        view.mouseDown(with: try mouse(.leftMouseDown, at: start, modifiers: .option))
        view.mouseDragged(with: try mouse(.leftMouseDragged, at: destination, modifiers: .option))
        XCTAssertNotEqual(view.pointOfView?.simdTransform, originalCamera)
        XCTAssertTrue(strokes.isEmpty)
        view.flagsChanged(with: try modifiers([]))
        XCTAssertFalse(cursor.isHidden)
        XCTAssertTrue(view.allowsCameraControl)
        var releasedCamera = view.pointOfView?.simdTransform
        view.mouseDragged(with: try mouse(.leftMouseDragged, at: start))
        XCTAssertEqual(view.pointOfView?.simdTransform, releasedCamera)
        view.flagsChanged(with: try modifiers(.option))
        view.mouseDragged(with: try mouse(.leftMouseDragged, at: destination, modifiers: .option))
        XCTAssertNotEqual(view.pointOfView?.simdTransform, releasedCamera)
        view.flagsChanged(with: try modifiers([]))
        releasedCamera = view.pointOfView?.simdTransform
        view.mouseUp(with: try mouse(.leftMouseUp, at: start))
        XCTAssertEqual(view.pointOfView?.simdTransform, releasedCamera)
        XCTAssertTrue(strokes.isEmpty, "Releasing Option during a drag must not paint")
        _ = view.snapshot()
        let point = centre(in: view)
        XCTAssertNotNil(view.textureBrush.sample(at: point, in: view))
        view.mouseDown(with: try mouse(.leftMouseDown, at: point))
        XCTAssertFalse(view.allowsCameraControl)
        view.mouseUp(with: try mouse(.leftMouseUp, at: point))
        XCTAssertEqual(strokes.count, 1)
        XCTAssertEqual(view.pointOfView?.simdTransform, releasedCamera)
        XCTAssertTrue(view.allowsCameraControl)
    }

    @MainActor
    func testPressingOptionDuringPaintingCommitsStrokeAndImmediatelyNavigates() throws {
        let view = makeView()
        var strokes: [TexturePriorityStroke] = []
        view.textureBrush.finished = { strokes.append($0) }
        let start = centre(in: view)
        view.mouseDown(with: try mouse(.leftMouseDown, at: start))
        let camera = view.pointOfView?.simdTransform
        view.flagsChanged(with: try modifiers(.option))
        XCTAssertEqual(strokes.count, 1)
        XCTAssertEqual(strokes.first?.samples.count, 1)
        let destination = NSPoint(x: start.x + 50, y: start.y + 20)
        view.mouseDragged(with: try mouse(.leftMouseDragged, at: destination, modifiers: .option))
        view.mouseUp(with: try mouse(.leftMouseUp, at: destination, modifiers: .option))
        XCTAssertNotEqual(view.pointOfView?.simdTransform, camera)
        XCTAssertEqual(strokes.count, 1)
        XCTAssertTrue(view.allowsCameraControl)
        view.flagsChanged(with: try modifiers([]))
        XCTAssertTrue(view.allowsCameraControl)
    }

    @MainActor
    func testOptionClickNeverPlacesCropOrSelectsAnArtifact() throws {
        let view = makeView()
        view.textureBrush.configuration = nil
        var placements = 0
        var selections = 0
        view.placeExclusion = { _ in placements += 1 }
        view.selection = { _ in selections += 1 }
        view.cameraInspection = CameraInspectionData(root: URL(fileURLWithPath: "/tmp"), cameras: [], photos: [])
        let start = centre(in: view)
        // The mouse event also handles Option already being down when the viewer gains focus.
        view.mouseDown(with: try mouse(.leftMouseDown, at: start, modifiers: .option))
        view.mouseUp(with: try mouse(.leftMouseUp, at: start, modifiers: .option))
        XCTAssertEqual(placements, 0)
        view.placeExclusion = nil
        view.mouseDown(with: try mouse(.leftMouseDown, at: start, modifiers: .option))
        view.flagsChanged(with: try modifiers([]))
        view.mouseUp(with: try mouse(.leftMouseUp, at: start))
        XCTAssertEqual(selections, 0)
        XCTAssertTrue(view.allowsCameraControl)
    }

    @MainActor
    func testOptionInterruptsCropAndShiftPansWithoutFurtherEdits() throws {
        let view = makeView()
        view.textureBrush.configuration = nil
        let region = ExclusionRegion()
        var edited = region
        var edits = 0
        view.cropHandles.update(in: view, regions: [region], selectedID: region.id) {
            edited = $0
            edits += 1
        }
        let start = centre(in: view)
        let destination = NSPoint(x: start.x + 30, y: start.y + 20)
        view.mouseDown(with: try mouse(.leftMouseDown, at: start))
        view.mouseDragged(with: try mouse(.leftMouseDragged, at: destination))
        XCTAssertNotEqual(edited, region)
        view.flagsChanged(with: try modifiers([.option, .shift]))
        let committed = edited
        let committedEdits = edits
        let cameraPosition = view.pointOfView?.simdPosition
        let cameraOrientation = view.pointOfView?.simdOrientation
        let pan = NSPoint(x: destination.x + 50, y: destination.y + 20)
        view.mouseDragged(with: try mouse(.leftMouseDragged, at: pan, modifiers: [.option, .shift]))
        view.mouseUp(with: try mouse(.leftMouseUp, at: pan, modifiers: [.option, .shift]))
        XCTAssertNotEqual(view.pointOfView?.simdPosition, cameraPosition)
        XCTAssertEqual(view.pointOfView?.simdOrientation, cameraOrientation)
        XCTAssertEqual(edited, committed)
        XCTAssertEqual(edits, committedEdits)
        XCTAssertTrue(view.allowsCameraControl)
    }

    @MainActor
    private func makeView() -> ArtifactSceneView {
        let scene = NativeSceneFactory().make(
            reconstruction: nil,
            points: [[-2.0, -2, 0], [2, 2, 0], [0, 0, 0]].map {
                CloudPoint(position: $0, colour: nil, error: 0)
            }, photos: [], root: URL(fileURLWithPath: "/tmp"))
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 800, height: 600))
        view.scene = scene
        view.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.fitArtifact()
        view.allowsCameraControl = true
        view.textureBrush.configuration = TextureBrushConfiguration()
        view.textureBrush.painting = TexturePriorityPainting(alignmentArtifact: "alignment-a")
        _ = view.snapshot()
        return view
    }

    @MainActor
    private func centre(in view: SCNView) -> NSPoint {
        let point = view.projectPoint(SCNVector3Zero)
        return NSPoint(x: point.x, y: view.isFlipped ? view.bounds.height - point.y : point.y)
    }

    private func mouse(
        _ type: NSEvent.EventType, at location: NSPoint, modifiers: NSEvent.ModifierFlags = []
    ) throws -> NSEvent {
        try XCTUnwrap(
            NSEvent.mouseEvent(
                with: type, location: location, modifierFlags: modifiers, timestamp: 0, windowNumber: 0,
                context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
    }

    private func modifiers(_ flags: NSEvent.ModifierFlags) throws -> NSEvent {
        try XCTUnwrap(
            NSEvent.keyEvent(
                with: .flagsChanged, location: .zero, modifierFlags: flags, timestamp: 0, windowNumber: 0,
                context: nil, characters: "", charactersIgnoringModifiers: "", isARepeat: false, keyCode: 58))
    }
}
