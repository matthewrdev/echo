import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class SparsePointPreviewTests: XCTestCase {
    func testQualityFilteringPrecedesSamplingAndCoversTheWholeCloud() {
        let points = (0..<101).map { index in
            CloudPoint(position: [Double(index), 0, 0], colour: nil, error: index.isMultiple(of: 2) ? 1 : 4)
        }
        let sampled = SparsePointPreview(
            points: points, options: SparseDisplayOptions(detail: .pointLimit(10), maximumReprojectionError: 1))
        XCTAssertEqual(sampled.eligibleCount, 51)
        XCTAssertEqual(sampled.points.count, 10)
        XCTAssertEqual(sampled.points.first?.position[0], 0)
        XCTAssertEqual(sampled.points.last?.position[0], 90)
        XCTAssertTrue(sampled.points.allSatisfy { $0.error == 1 })
        let all = SparsePointPreview(points: points, options: SparseDisplayOptions(detail: .percentage(100)))
        XCTAssertEqual(all.points.count, 101)
        let none = SparsePointPreview(points: points, options: SparseDisplayOptions(maximumReprojectionError: 0.5))
        XCTAssertTrue(none.points.isEmpty)
        let invalid = CloudPoint(position: [.infinity, 0, 0], colour: nil, error: 0)
        XCTAssertTrue(SparsePointPreview(points: [invalid], options: SparseDisplayOptions()).points.isEmpty)
    }

    func testHiddenTracksKeepTheirObservationsButAreExcludedFromSelectionAndCameraLists() {
        let points = [
            CloudPoint(
                position: [1, 2, 3], colour: nil, error: 0.4, imageA: "a", imageB: "b", pixelA: [10, 20],
                pixelB: [30, 40]),
            CloudPoint(
                position: [1, 2, 3], colour: nil, error: 3, imageA: "a", imageB: "c", pixelA: [10, 20],
                pixelB: [50, 60]),
            CloudPoint(
                position: [4, 5, 6], colour: nil, error: 4, imageA: "b", imageB: "c", pixelA: [1, 2], pixelB: [3, 4]),
        ]
        let scene = ReconstructionScene(
            frame: "local", origin: nil, cameras: [], points: points, unplaced: [], warnings: [])
        let data = SparseInspectionData(
            root: URL(fileURLWithPath: "/tmp"), scene: scene, photos: [], featureFiles: [:], visiblePoints: [points[0]])
        XCTAssertEqual(data.visibleTrackIndices, [0])
        XCTAssertEqual(data.tracks[0].observations.map(\.imageID), ["a", "b", "c"])
        XCTAssertEqual(data.cameraTracks["b"], [0])
        XCTAssertEqual(data.cameraTracks["c"], [0])
    }

    func testSavedSparsePreviewCountsAndFilteredRendering() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_PROJECT"], let runID = environment["ECHO_TEST_RUN"] else {
            throw XCTSkip("Set a saved project and run to inspect sparse display filters")
        }
        let root = URL(fileURLWithPath: path)
        let run = try JSONDecoder().decode(
            RunData.self, from: Data(contentsOf: root.appendingPathComponent("runs/\(runID)/run.json")))
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        for maximumError in [Double?.none, 1.0] {
            let value = try await reader.load(
                root: root, run: run, stage: .alignment, unit: nil, mode: "", slice: 0,
                sparseOptions: SparseDisplayOptions(detail: .percentage(100), maximumReprojectionError: maximumError))
            let inspection = try XCTUnwrap(value.sparseInspection)
            let scene = try XCTUnwrap(value.scene)
            let count = try XCTUnwrap(
                scene.rootNode.childNode(withName: "artifact", recursively: false)?.geometry?
                    .sources(for: .vertex).first?.vectorCount)
            XCTAssertTrue(value.detail.contains("displaying \(count.formatted()) of"))
            XCTAssertGreaterThan(count, 0)
            XCTAssertGreaterThan(inspection.visibleTrackIndices.count, 0)
            if run.artifacts["alignment"] == nil {
                XCTAssertTrue(value.detail.contains("reconstructed · provisional"))
            }
            if maximumError != nil {
                XCTAssertTrue(value.detail.contains("pass error filter"))
                XCTAssertLessThan(inspection.visibleTrackIndices.count, inspection.tracks.count)
            }
            if let output = environment["ECHO_TEST_RENDER_DIR"], let device = MTLCreateSystemDefaultDevice() {
                let renderer = SCNRenderer(device: device, options: nil)
                renderer.scene = scene
                renderer.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
                let image = renderer.snapshot(
                    atTime: 0, with: CGSize(width: 1100, height: 700), antialiasingMode: .multisampling4X)
                let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
                try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
                let name = maximumError == nil ? "sparse-all.png" : "sparse-filtered.png"
                try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                    to: URL(fileURLWithPath: output).appendingPathComponent(name))
            }
        }
    }
}
