import AppKit
import EchoRendering
import SceneKit
import XCTest
import simd

@testable import EchoDesktop

final class CropHandleTests: XCTestCase {
    @MainActor
    func testMoveUsesHorizontalPlaneWithoutChangingHeightOrCamera() throws {
        let view = makeView(direction: .top)
        var region = ExclusionRegion()
        region.centreY = 0.4
        let handle = try handle(.move, region)
        let start = screen(handle.position, in: view)
        let drag = CropHandleDrag(region: region, handle: handle, start: start, view: view)
        let camera = view.pointOfView?.simdTransform
        let destination = handle.position + SIMD3(0.7, 0, -0.6)
        let moved = try XCTUnwrap(drag.region(at: screen(destination, in: view), view: view))
        XCTAssertEqual(moved.centreX, 0.7, accuracy: 0.0001)
        XCTAssertEqual(moved.centreY, 0.4)
        XCTAssertEqual(moved.centreZ, -0.6, accuracy: 0.0001)
        XCTAssertEqual(moved.width, region.width)
        XCTAssertEqual(view.pointOfView?.simdTransform, camera)
    }

    @MainActor
    func testFrontViewCanMoveInDepthAndResizeAnEndOnAxis() throws {
        let view = makeView()
        let region = ExclusionRegion()
        let centre = try handle(.move, region)
        let start = screen(centre.position, in: view)
        let drag = CropHandleDrag(region: region, handle: centre, start: start, view: view)
        let moved = try XCTUnwrap(drag.region(at: NSPoint(x: start.x + 30, y: start.y + 40), view: view))
        XCTAssertGreaterThan(moved.centreX, 0)
        XCTAssertLessThan(moved.centreZ, 0)
        XCTAssertEqual(moved.centreY, 0)
        let depth = try handle(.depth, region)
        let depthStart = screen(depth.position, in: view)
        let resize = CropHandleDrag(region: region, handle: depth, start: depthStart, view: view)
        let resized = try XCTUnwrap(resize.region(at: NSPoint(x: depthStart.x, y: depthStart.y + 30), view: view))
        XCTAssertGreaterThan(resized.depth, region.depth)
        XCTAssertEqual(resized.width, region.width)
        XCTAssertEqual(resized.centreZ, region.centreZ)
    }

    @MainActor
    func testResizeFollowsRotatedBoxAxesAndKeepsTheCentreFixed() throws {
        let view = makeView(direction: .top)
        var region = ExclusionRegion()
        region.rotationY = 37
        for kind in [CropHandleKind.width, .depth] {
            let handle = try handle(kind, region)
            let drag = CropHandleDrag(
                region: region, handle: handle, start: screen(handle.position, in: view), view: view)
            let target = handle.position + region.orientation.act(kind.axis) * 0.3
            let resized = try XCTUnwrap(drag.region(at: screen(target, in: view), view: view))
            XCTAssertEqual(kind == .width ? resized.width : resized.depth, 1.6, accuracy: 0.0001)
            XCTAssertEqual(kind == .width ? resized.depth : resized.width, 1)
            XCTAssertEqual(resized.centreX, region.centreX)
            XCTAssertEqual(resized.centreZ, region.centreZ)
            XCTAssertEqual(resized.rotationY, 37)
            let collapsed = handle.position - region.orientation.act(kind.axis) * 4
            let smallest = try XCTUnwrap(drag.region(at: screen(collapsed, in: view), view: view))
            XCTAssertTrue(smallest.isValid)
            XCTAssertGreaterThan(kind == .width ? smallest.width : smallest.depth, 0)
        }
    }

    @MainActor
    func testCylinderResizesRadiusAndHeightWithoutRotation() throws {
        let view = makeView()
        var region = ExclusionRegion(shape: .cylinder)
        region.rotationY = 70
        XCTAssertEqual(Set(CropHandle.handles(for: region).map(\.kind)), [.move, .radius, .height])
        for kind in [CropHandleKind.radius, .height] {
            let handle = try handle(kind, region)
            let drag = CropHandleDrag(
                region: region, handle: handle, start: screen(handle.position, in: view), view: view)
            let target = handle.position + kind.axis * 0.3
            let resized = try XCTUnwrap(drag.region(at: screen(target, in: view), view: view))
            XCTAssertEqual(
                kind == .radius ? resized.radius : resized.height, kind == .radius ? 0.8 : 1.6, accuracy: 0.0001)
            XCTAssertEqual(resized.centreY, region.centreY)
        }
        let rotation = CropHandle(kind: .rotate, position: SIMD3(1, 0, 1))
        let drag = CropHandleDrag(
            region: region, handle: rotation, start: screen(rotation.position, in: view), view: view)
        XCTAssertNil(drag.region(at: NSPoint(x: 100, y: 100), view: view))
    }

    @MainActor
    func testBoxRotationWrapsAndIsOnlyAroundVerticalAxis() throws {
        let view = makeView(direction: .top)
        var region = ExclusionRegion()
        region.rotationY = 170
        let handle = try handle(.rotate, region)
        let drag = CropHandleDrag(region: region, handle: handle, start: screen(handle.position, in: view), view: view)
        let target = simd_quatd(angle: .pi / 2, axis: SIMD3(0, 1, 0)).act(handle.position)
        let rotated = try XCTUnwrap(drag.region(at: screen(target, in: view), view: view))
        XCTAssertEqual(rotated.rotationY, -100, accuracy: 0.001)
        XCTAssertEqual(simd_length(rotated.orientation.act(SIMD3(0, 1, 0)) - SIMD3(0, 1, 0)), 0, accuracy: 1e-10)
        XCTAssertEqual(rotated.width, region.width)
        XCTAssertEqual(rotated.centreY, region.centreY)
    }

    @MainActor
    func testMouseGestureUpdatesRegionAndEscapeRestoresItWithoutMovingCamera() throws {
        let view = makeView(direction: .top)
        let region = ExclusionRegion()
        var edited = region
        var selections = 0
        view.selection = { _ in selections += 1 }
        view.cropHandles.update(in: view, regions: [region], selectedID: region.id, changed: { edited = $0 })
        let centre = screen(try handle(.move, region).position, in: view)
        let destination = NSPoint(x: centre.x + 40, y: centre.y + 30)
        let camera = view.pointOfView?.simdTransform
        view.allowsCameraControl = true
        XCTAssertEqual(view.cropHandles.pick(at: centre, in: view)?.kind, .move)
        view.mouseDown(with: try event(.leftMouseDown, at: centre))
        XCTAssertFalse(view.allowsCameraControl)
        view.mouseDragged(with: try event(.leftMouseDragged, at: destination))
        XCTAssertNotEqual(edited, region)
        let escape = try XCTUnwrap(
            NSEvent.keyEvent(
                with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: 0,
                context: nil, characters: "\u{1b}", charactersIgnoringModifiers: "\u{1b}", isARepeat: false, keyCode: 53
            ))
        view.keyDown(with: escape)
        XCTAssertEqual(edited, region)
        view.mouseUp(with: try event(.leftMouseUp, at: destination))
        XCTAssertTrue(view.allowsCameraControl)
        XCTAssertEqual(edited, region)
        XCTAssertEqual(view.pointOfView?.simdTransform, camera)
        XCTAssertEqual(selections, 0)
        XCTAssertFalse(view.cropHandles.begin(at: NSPoint(x: 2, y: 2), in: view))
        view.mouseDown(with: try event(.leftMouseDown, at: centre))
        view.mouseDragged(with: try event(.leftMouseDragged, at: destination))
        let firstEdit = edited
        // SwiftUI sends the changed region back while the same drag is still in progress.
        view.cropHandles.update(in: view, regions: [edited], selectedID: edited.id, changed: { edited = $0 })
        view.mouseUp(with: try event(.leftMouseUp, at: NSPoint(x: destination.x + 20, y: destination.y)))
        XCTAssertGreaterThan(edited.centreX, firstEdit.centreX)
        XCTAssertEqual(edited.centreY, region.centreY)
        XCTAssertEqual(view.pointOfView?.simdTransform, camera)
        XCTAssertTrue(view.allowsCameraControl)
    }

    @MainActor
    func testClosingEditorDuringDragRemovesHandlesAndDoesNotEditOldRegion() throws {
        let view = makeView(direction: .top)
        let region = ExclusionRegion()
        var edits = 0
        let artifact = try XCTUnwrap(view.scene?.rootNode.childNode(withName: "artifact", recursively: false))
        let originalBounds = artifact.boundingBox
        view.cropHandles.update(in: view, regions: [region], selectedID: region.id, changed: { _ in edits += 1 })
        let root = try XCTUnwrap(view.scene?.rootNode.childNode(withName: "crop-handles", recursively: false))
        XCTAssertEqual(root.childNodes.filter { $0.geometry?.elements.first?.primitiveType == .point }.count, 5)
        XCTAssertEqual(artifact.boundingBox.min.x, originalBounds.min.x)
        XCTAssertEqual(artifact.boundingBox.max.x, originalBounds.max.x)
        XCTAssertEqual(try CropPointCounter(scene: view.scene).count(regions: []).total, 2)
        let centre = screen(try handle(.move, region).position, in: view)
        XCTAssertTrue(view.cropHandles.begin(at: centre, in: view))
        view.cropHandles.update(in: view, regions: [], selectedID: nil, changed: nil)
        XCTAssertTrue(view.cropHandles.end(at: NSPoint(x: centre.x + 40, y: centre.y), in: view))
        XCTAssertEqual(edits, 0)
        XCTAssertNil(view.scene?.rootNode.childNode(withName: "crop-handles", recursively: false))
        var disabled = region
        disabled.enabled = false
        view.cropHandles.update(in: view, regions: [disabled], selectedID: disabled.id, changed: { _ in })
        XCTAssertNil(view.cropHandles.pick(at: centre, in: view))
    }

    @MainActor
    private func makeView(direction: SceneViewDirection = .north) -> ArtifactSceneView {
        let scene = NativeSceneFactory().make(
            reconstruction: nil,
            points: [[-2.0, -2, -2], [2, 2, 2]].map { CloudPoint(position: $0, colour: nil, error: 0) },
            photos: [], root: URL(fileURLWithPath: "/tmp"))
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 800, height: 600))
        view.scene = scene
        view.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.fitArtifact(direction: direction)
        SCNTransaction.flush()
        return view
    }

    private func handle(_ kind: CropHandleKind, _ region: ExclusionRegion) throws -> CropHandle {
        try XCTUnwrap(CropHandle.handles(for: region).first { $0.kind == kind })
    }

    @MainActor
    private func screen(_ point: SIMD3<Double>, in view: SCNView) -> NSPoint {
        let projected = view.projectPoint(SCNVector3(point.x, point.y, point.z))
        return NSPoint(x: projected.x, y: view.isFlipped ? view.bounds.height - projected.y : projected.y)
    }

    private func event(_ type: NSEvent.EventType, at location: NSPoint) throws -> NSEvent {
        try XCTUnwrap(
            NSEvent.mouseEvent(
                with: type, location: location, modifierFlags: [], timestamp: 0, windowNumber: 0,
                context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
    }
}
