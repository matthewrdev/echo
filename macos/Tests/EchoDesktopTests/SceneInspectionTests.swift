import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class SceneInspectionTests: XCTestCase {
    func testSparseInspectorRecoversAllObservationsOfRepeatedTrack() {
        let points = [
            CloudPoint(
                position: [1, 2, 3], colour: nil, error: 0.4, imageA: "a", imageB: "b", pixelA: [10, 20],
                pixelB: [30, 40]),
            CloudPoint(
                position: [1, 2, 3], colour: nil, error: 0.6, imageA: "a", imageB: "c", pixelA: [10, 20],
                pixelB: [50, 60]),
            CloudPoint(
                position: [1, 2, 3], colour: nil, error: 0.4, imageA: "a", imageB: "b", pixelA: [10, 20],
                pixelB: [30, 40]),
            CloudPoint(
                position: [4, 5, 6], colour: nil, error: 0.2, imageA: "b", imageB: "c", pixelA: [1, 2], pixelB: [3, 4]),
            CloudPoint(position: [0, 0, 0], colour: nil, error: 0),
        ]
        let scene = ReconstructionScene(
            frame: "local", origin: nil, cameras: [], points: points, unplaced: [], warnings: [])
        let data = SparseInspectionData(root: URL(fileURLWithPath: "/tmp"), scene: scene, photos: [], featureFiles: [:])
        XCTAssertEqual(data.tracks.count, 2)
        XCTAssertEqual(data.tracks[0].observations.map(\.imageID), ["a", "b", "c"])
        XCTAssertEqual(data.tracks[0].observations[2].pixel, [50, 60])
        XCTAssertEqual(data.tracks[0].observations[2].error, 0.6)
        XCTAssertEqual(data.cameraTracks["a"], [0])
        XCTAssertEqual(data.cameraTracks["b"], [0, 1])
        XCTAssertEqual(data.cameraTracks["c"], [0, 1])
    }

    func testFitIncludesEveryBoundingCornerInPortraitAndLandscape() {
        let bounds = SceneBounds(positions: [[-80, -10, -40], [20, 40, 10]])
        for direction in [SceneViewDirection.north, .east, .top] {
            for size in [CGSize(width: 1000, height: 400), CGSize(width: 300, height: 900)] {
                let camera = SCNNode()
                camera.camera = SCNCamera()
                bounds.fit(camera: camera, size: size, direction: direction)
                let screenUp = camera.convertVector(direction.up, from: nil)
                XCTAssertEqual(screenUp.y, 1, accuracy: 0.000001)
                let tangent = tan(25 * Double.pi / 180)
                for x in [bounds.minimum.x, bounds.maximum.x] {
                    for y in [bounds.minimum.y, bounds.maximum.y] {
                        for z in [bounds.minimum.z, bounds.maximum.z] {
                            let p = camera.convertPosition(SCNVector3(x, y, z), from: nil)
                            XCTAssertLessThan(p.z, 0)
                            XCTAssertLessThan(abs(p.y / -p.z), tangent)
                            XCTAssertLessThan(abs(p.x / -p.z), tangent * size.width / size.height)
                        }
                    }
                }
            }
        }

    }

    func testGridHasUnitSpacingAndDoesNotAffectCameraFit() throws {
        let bounds = SceneBounds(positions: [[-2, 0, -2], [2, 4, 2]])
        let grid = GroundGridFactory().make(bounds: bounds)
        XCTAssertTrue(grid.isHidden)
        XCTAssertEqual(grid.geometry?.elements.first?.primitiveType, .line)
        XCTAssertEqual(grid.geometry?.firstMaterial?.lightingModel, .constant)
        XCTAssertEqual(grid.geometry?.firstMaterial?.writesToDepthBuffer, false)
        let source = try XCTUnwrap(grid.geometry?.sources(for: .vertex).first)
        let x0 = source.data.withUnsafeBytes { $0.loadUnaligned(fromByteOffset: source.dataOffset, as: Float.self) }
        let x1 = source.data.withUnsafeBytes {
            $0.loadUnaligned(fromByteOffset: source.dataOffset + 4 * source.dataStride, as: Float.self)
        }
        XCTAssertEqual(x1 - x0, 1, accuracy: 0.00001)
        let scene = NativeSceneFactory().make(
            reconstruction: nil,
            points: [
                CloudPoint(position: [-2, 0, -2], colour: nil, error: 0),
                CloudPoint(position: [2, 4, 2], colour: nil, error: 0),
            ],
            photos: [], root: URL(fileURLWithPath: "/tmp"))
        let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        XCTAssertLessThan(camera.position.z, 10)
        XCTAssertNotNil(scene.rootNode.childNode(withName: "ground-grid", recursively: false))
    }

    func testGridRendersTransparentCellsAndSubtleUnlitLines() throws {
        guard let device = MTLCreateSystemDefaultDevice() else {
            throw XCTSkip("Native rendering requires a Metal device.")
        }
        let scene = SCNScene()
        scene.background.contents = NSColor.white
        let grid = GroundGridFactory().make(bounds: SceneBounds(positions: [[-2, 0, -2], [2, 0, 2]]))
        grid.isHidden = false
        scene.rootNode.addChildNode(grid)
        let camera = SCNNode()
        camera.camera = SCNCamera()
        camera.camera?.usesOrthographicProjection = true
        camera.camera?.orthographicScale = 2
        camera.position = SCNVector3(0, 10, 0)
        camera.eulerAngles.x = -.pi / 2
        scene.rootNode.addChildNode(camera)
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.autoenablesDefaultLighting = false
        renderer.scene = scene
        renderer.pointOfView = camera
        let rendered = renderer.snapshot(
            atTime: 0, with: CGSize(width: 256, height: 256), antialiasingMode: .multisampling4X)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(rendered.tiffRepresentation)))
        let cell = try XCTUnwrap(bitmap.colorAt(x: 160, y: 160)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(cell.redComponent, 0.99, "Cell interiors must show the unchanged background")
        var minimum = CGFloat(1)
        for x in 124...132 {
            let color = try XCTUnwrap(bitmap.colorAt(x: x, y: 160)?.usingColorSpace(.deviceRGB))
            minimum = min(minimum, color.redComponent)
        }
        XCTAssertLessThan(minimum, 0.99, "A grid line must be visible")
        XCTAssertGreaterThan(minimum, 0.75, "Grid lines must remain translucent")
    }

    func testInspectorOpensSavedSparseArtifactWithoutReconstruction() async throws {
        guard let path = ProcessInfo.processInfo.environment["ECHO_TEST_PROJECT"],
            let runID = ProcessInfo.processInfo.environment["ECHO_TEST_RUN"]
        else { throw XCTSkip("Saved artifact not configured") }
        let root = URL(fileURLWithPath: path)
        let run = try JSONDecoder().decode(
            RunData.self, from: Data(contentsOf: root.appendingPathComponent("runs/\(runID)/run.json")))
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let presentation = try await reader.load(
            root: root, run: run, stage: .alignment, unit: nil, mode: "", slice: 0.5)
        let inspection = try XCTUnwrap(presentation.sparseInspection)
        XCTAssertFalse(inspection.tracks.isEmpty)
        XCTAssertTrue(inspection.tracks.allSatisfy { $0.observations.count >= 2 })
        XCTAssertTrue(inspection.tracks.flatMap(\.observations).allSatisfy { inspection.photos[$0.imageID] != nil })
        XCTAssertNotNil(presentation.scene?.rootNode.childNode(withName: "ground-grid", recursively: false))
        if let output = ProcessInfo.processInfo.environment["ECHO_TEST_RENDER_DIR"],
            let device = MTLCreateSystemDefaultDevice(), let scene = presentation.scene,
            let camera = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
        {
            let bounds = SceneBounds(positions: inspection.tracks.map(\.position))
            let size = CGSize(width: 1100, height: 700)
            bounds.fit(camera: camera, size: size)
            camera.position = SCNVector3(
                bounds.centre.x, bounds.centre.y + bounds.span * 0.6,
                bounds.centre.z + bounds.span * 1.2)
            camera.look(at: bounds.centre)
            scene.rootNode.childNode(withName: "ground-grid", recursively: false)?.isHidden = false
            let renderer = SCNRenderer(device: device, options: nil)
            let gridVisibility = GroundGridVisibility()
            renderer.delegate = gridVisibility
            renderer.autoenablesDefaultLighting = false
            renderer.scene = scene
            renderer.pointOfView = camera
            let snapshot = renderer.snapshot(atTime: 0, with: size, antialiasingMode: .multisampling4X)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(snapshot.tiffRepresentation)))
            try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: URL(fileURLWithPath: output).appendingPathComponent("sparse-grid.png"))
        }
    }
}
