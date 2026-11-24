import AppKit
import EchoRendering
import Metal
import SceneKit
import XCTest
import simd

@testable import EchoDesktop

final class PaintedCropTests: XCTestCase {
    func testNewToolsDefaultToInclude() {
        XCTAssertEqual(ExclusionRegion().mode, .include)
        XCTAssertEqual(ExclusionRegion(shape: .cylinder).mode, .include)
        XCTAssertEqual(CropBrushConfiguration().mode, .include)
    }

    @MainActor
    func testProjectionIncludesDepthAndSurvivesCameraChangesAndSerialization() throws {
        for orthographic in [false, true] {
            let view = makeView()
            view.pointOfView?.camera?.usesOrthographicProjection = orthographic
            view.pointOfView?.camera?.orthographicScale = 3
            let polygon = [
                CGPoint(x: 320, y: 210), CGPoint(x: 480, y: 210), CGPoint(x: 480, y: 390), CGPoint(x: 320, y: 390),
            ]
            let region = try ProjectedCropHullBuilder().make(points: polygon, in: view, mode: .include)
            XCTAssertTrue(region.isValid)
            let geometry = CropRegionGeometry(region)
            for z in [-1.0, 0, 1] { XCTAssertTrue(geometry.contains(SIMD3(0, 0, z))) }
            XCTAssertFalse(geometry.contains(SIMD3(2, 0, 0)))
            XCTAssertFalse(geometry.contains(SIMD3(0, 0, 5)))
            let restored = try JSONDecoder().decode(ExclusionRegion.self, from: JSONEncoder().encode(region))
            XCTAssertEqual(restored, region)
            view.fitArtifact(direction: .top)
            XCTAssertTrue(CropRegionGeometry(restored).contains(.zero))
            XCTAssertTrue(CropHandle.handles(for: region).isEmpty)
        }
    }

    @MainActor
    func testSmallBrushProjectsInsideLargeCloudFromTranslatedCamera() throws {
        for orthographic in [false, true] {
            let view = makeView()
            let artifact = try XCTUnwrap(view.scene?.rootNode.childNode(withName: "artifact", recursively: false))
            artifact.scale = SCNVector3(1000, 1000, 1000)
            let camera = try XCTUnwrap(view.pointOfView)
            camera.position = SCNVector3(800, 300, 700)
            camera.look(at: SCNVector3(400, 100, 0))
            camera.camera?.zNear = 0.0001
            camera.camera?.zFar = 1_000_000
            camera.camera?.usesOrthographicProjection = orthographic
            camera.camera?.orthographicScale = 600
            SCNTransaction.flush()
            for radius in [2.0, 14, 80] {
                var polygon = ScreenCropHull().adding(CGPoint(x: 400, y: 300), radius: radius, to: [])
                polygon = ScreenCropHull().adding(CGPoint(x: 650, y: 350), radius: radius, to: polygon)
                let region = try ProjectedCropHullBuilder().make(points: polygon, in: view, mode: .include)
                XCTAssertTrue(region.isValid)
                XCTAssertEqual(region.mode, .include)
                let restored = try JSONDecoder().decode(ExclusionRegion.self, from: JSONEncoder().encode(region))
                XCTAssertTrue(restored.isValid)
            }
        }
    }

    @MainActor
    func testStrokeBuildsFilledHullAndEscapeCancelsWithoutMovingCamera() throws {
        let view = makeView()
        var completed: [ExclusionRegion] = []
        var error: String?
        view.cropBrush.configuration = CropBrushConfiguration(radius: 10)
        view.cropBrush.finished = { completed.append($0) }
        view.cropBrush.failed = { error = $0 }
        let camera = view.pointOfView?.simdTransform
        let points = [
            CGPoint(x: 300, y: 200), CGPoint(x: 500, y: 200), CGPoint(x: 500, y: 400), CGPoint(x: 300, y: 400),
        ]
        view.mouseDown(with: try event(.leftMouseDown, at: points[0]))
        for point in points.dropFirst() { view.mouseDragged(with: try event(.leftMouseDragged, at: point)) }
        view.mouseUp(with: try event(.leftMouseUp, at: points[0]))
        XCTAssertNil(error)
        XCTAssertEqual(completed.count, 1)
        XCTAssertEqual(completed.first?.mode, .include)
        XCTAssertTrue(try XCTUnwrap(completed.first?.hull).contains(.zero))
        XCTAssertEqual(view.pointOfView?.simdTransform, camera)
        XCTAssertTrue(view.allowsCameraControl)
        view.mouseDown(with: try event(.leftMouseDown, at: points[0]))
        XCTAssertTrue(view.cropBrush.cancel(in: view))
        view.mouseUp(with: try event(.leftMouseUp, at: points[1]))
        XCTAssertEqual(completed.count, 1)
        XCTAssertTrue(view.allowsCameraControl)
    }

    @MainActor
    func testHandleDraggingWhileRenderingRetainsOnlyOwnedOverlays() async throws {
        let view = makeView()
        let scene = try XCTUnwrap(view.scene)
        let camera = try XCTUnwrap(view.pointOfView)
        scene.rootNode.childNode(withName: "ground-grid", recursively: false)?.isHidden = false
        let region = ExclusionRegion()
        var edited = region
        view.cropHandles.update(in: view, regions: [region], selectedID: region.id, changed: { edited = $0 })
        let projected = view.projectPoint(SCNVector3(0, 0, 0))
        let start = CGPoint(x: projected.x, y: projected.y)
        XCTAssertTrue(view.cropHandles.begin(at: start, in: view))
        let worker = Task.detached {
            let renderer = SCNRenderer(device: MTLCreateSystemDefaultDevice())
            renderer.scene = scene
            renderer.pointOfView = camera
            let delegate = GroundGridVisibility()
            renderer.delegate = delegate
            for index in 0..<20 {
                _ = renderer.snapshot(
                    atTime: Double(index) / 60, with: CGSize(width: 200, height: 150), antialiasingMode: .none)
            }
        }
        for index in 0..<250 {
            let point = CGPoint(x: start.x + Double(index % 50), y: start.y + Double(index % 30))
            XCTAssertTrue(view.cropHandles.move(to: point, in: view))
            view.cropHandles.update(in: view, regions: [edited], selectedID: edited.id, changed: { edited = $0 })
            SCNTransaction.flush()
        }
        XCTAssertTrue(view.cropHandles.end(at: start, in: view))
        await worker.value
        XCTAssertEqual(scene.rootNode.childNodes.filter { $0.name == "crop-handles" }.count, 1)
        XCTAssertEqual(scene.rootNode.childNodes.filter { $0.name == "exclusion-regions" }.count, 1)
        view.exclusionOverlay.update(scene: scene, regions: [edited], selectedID: edited.id)
        SceneViewport.dismantleNSView(view, coordinator: ())
        XCTAssertNil(scene.rootNode.childNode(withName: "crop-handles", recursively: false))
        XCTAssertNil(scene.rootNode.childNode(withName: "exclusion-regions", recursively: false))
    }

    @MainActor
    func testPaintedHullOnSavedDenseCloud() async throws {
        let env = ProcessInfo.processInfo.environment
        guard let project = env["ECHO_TEST_CLOUD_PROJECT"], let runID = env["ECHO_TEST_CLOUD_RUN"],
            let output = env["ECHO_TEST_PAINT_OUTPUT"]
        else { throw XCTSkip("Set a saved dense cloud and paint output folder") }
        let root = URL(fileURLWithPath: project)
        let reader = ArtifactJSONReader()
        let run = try await SavedRunFixture.read(root, id: runID)
        let folder = root.appendingPathComponent(try XCTUnwrap(run.artifacts["cloud"]))
        let report: DenseCloudReport = try reader.read(folder.appendingPathComponent("cloud.bin"))
        let cloud = try DenseCloudPreviewReader().read(
            file: folder.appendingPathComponent("cloud.ply"), expectedCount: report.retainedSamples,
            limit: 1_000_000, minimumSupport: 0)
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 1440, height: 1000))
        view.scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: root, cloudGeometry: cloud.geometry)
        view.pointOfView = view.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.fitArtifact()
        var polygon: [CGPoint] = []
        for point in [CGPoint(x: 550, y: 350), CGPoint(x: 900, y: 400), CGPoint(x: 800, y: 650)] {
            polygon = ScreenCropHull().adding(point, radius: 14, to: polygon)
        }
        let region = try ProjectedCropHullBuilder().make(points: polygon, in: view, mode: .include)
        XCTAssertTrue(region.isValid)
        let counts = try CropPointCounter(scene: view.scene).count(regions: [region])
        XCTAssertGreaterThan(counts.included, 0)
        XCTAssertGreaterThan(counts.excluded, 0)
        view.exclusionOverlay.update(scene: view.scene, regions: [region], selectedID: region.id)
        let renderer = SCNRenderer(device: try XCTUnwrap(MTLCreateSystemDefaultDevice()))
        renderer.scene = view.scene
        renderer.pointOfView = view.pointOfView
        let rendered = renderer.snapshot(atTime: 0, with: view.bounds.size, antialiasingMode: .multisampling4X)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(rendered.tiffRepresentation)))
        let destination = URL(fileURLWithPath: output)
        try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
            to: destination.appendingPathComponent("painted-cloud.png"))
        try JSONEncoder().encode(region).write(to: destination.appendingPathComponent("painted-cloud-region.json"))
    }

    @MainActor
    func testPaintedHullOnSavedModel() async throws {
        let env = ProcessInfo.processInfo.environment
        guard let project = env["ECHO_TEST_PROJECT"], let runID = env["ECHO_TEST_RUN"],
            let output = env["ECHO_TEST_PAINT_OUTPUT"]
        else { throw XCTSkip("Set a saved model and paint output folder") }
        let root = URL(fileURLWithPath: project)
        let run = try await SavedRunFixture.read(root, id: runID)
        let artifact = try GeoreferenceArtifactReader().load(root: root, run: run)
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 1440, height: 1000))
        view.scene = artifact.presentation.scene
        view.pointOfView = view.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.fitArtifact()
        var polygon: [CGPoint] = []
        for point in [
            CGPoint(x: 520, y: 350), CGPoint(x: 920, y: 350), CGPoint(x: 960, y: 700), CGPoint(x: 500, y: 700),
        ] {
            polygon = ScreenCropHull().adding(point, radius: 12, to: polygon)
        }
        let region = try ProjectedCropHullBuilder().make(points: polygon, in: view, mode: .include)
        let counts = try CropPointCounter(scene: view.scene).count(regions: [region])
        XCTAssertGreaterThan(counts.included, 0)
        XCTAssertGreaterThan(counts.excluded, 0)
        view.exclusionOverlay.update(scene: view.scene, regions: [region], selectedID: region.id)
        let renderer = SCNRenderer(device: try XCTUnwrap(MTLCreateSystemDefaultDevice()))
        renderer.scene = view.scene
        renderer.pointOfView = view.pointOfView
        let rendered = renderer.snapshot(atTime: 0, with: view.bounds.size, antialiasingMode: .multisampling4X)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(rendered.tiffRepresentation)))
        let folder = URL(fileURLWithPath: output)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
            to: folder.appendingPathComponent("painted-hull.png"))
        var settings = ProcessingSettings()
        settings.exclusionRegions = [region]
        try JSONEncoder().encode(settings).write(to: folder.appendingPathComponent("painted-settings.json"))
    }

    @MainActor
    private func makeView() -> ArtifactSceneView {
        let surface = Surface(
            vertices: [
                [-2, -2, -1], [2, -2, -1], [2, 2, -1], [-2, 2, -1],
                [-2, -2, 1], [2, -2, 1], [2, 2, 1], [-2, 2, 1],
            ],
            faces: [[0, 1, 2], [0, 2, 3], [4, 5, 6], [4, 6, 7]], sourceImages: [])
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 800, height: 600))
        view.scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: URL(fileURLWithPath: "/tmp"), surface: surface)
        view.pointOfView = view.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.fitArtifact(direction: .north)
        view.allowsCameraControl = true
        return view
    }

    private func event(_ type: NSEvent.EventType, at location: NSPoint) throws -> NSEvent {
        try XCTUnwrap(
            NSEvent.mouseEvent(
                with: type, location: location, modifierFlags: [], timestamp: 0,
                windowNumber: 0, context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
    }
}
