import AppKit
import XCTest

@testable import EchoDesktop

final class SavedDepthArtifactReaderTests: XCTestCase {
    func testModesAndRecentPhotosReuseImagesButChangedFilesInvalidateThem() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let alignment = root.appendingPathComponent("scene.json")
        try Data(#"{"frame":"arbitrary","origin":null,"cameras":[],"points":"unused"}"#.utf8).write(to: alignment)
        let photos = (1...4).map { photo("p\($0)") }
        let units = try photos.map { photo in
            let file = root.appendingPathComponent(photo.id + ".json")
            try writeDepth(file, imageID: photo.id, value: 12.5)
            return ArtifactUnit(
                id: photo.id, title: photo.name, file: file, imageIDs: [photo.id], thumbnails: [], detail: "")
        }
        let run = RunData(
            id: "run", status: "cancelled", target: "depth",
            snapshot: ProjectData(
                id: "project", name: "Test", revision: 1, images: photos, settings: ProcessingSettings(), hints: []),
            artifacts: ["alignment": ".", "depthPreview": "."], error: nil, progress: nil)
        let reader = SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer())
        let first = try await reader.load(root: root, run: run, unit: units[0], mode: "Depth")
        let depth = try XCTUnwrap(first.depthImage)
        let confidence = try await reader.load(root: root, run: run, unit: units[0], mode: "Confidence")
        XCTAssertFalse(depth.isMetric)
        XCTAssertEqual(confidence.depthImage?.map.depth, depth.map.depth)
        XCTAssertFalse(try XCTUnwrap(confidence.depthImage).overlay === depth.overlay)
        _ = try await reader.load(root: root, run: run, unit: units[1], mode: "Depth")
        let repeated = try await reader.load(root: root, run: run, unit: units[0], mode: "Depth")
        XCTAssertTrue(try XCTUnwrap(repeated.depthImage).overlay === depth.overlay)
        let repeatedConfidence = try await reader.load(root: root, run: run, unit: units[0], mode: "Confidence")
        XCTAssertTrue(try XCTUnwrap(repeatedConfidence.depthImage).overlay === confidence.depthImage?.overlay)

        try writeDepth(units[0].file, imageID: photos[0].id, value: 123.25)
        let changed = try await reader.load(root: root, run: run, unit: units[0], mode: "Depth")
        XCTAssertEqual(changed.depthImage?.map.depth, [123.25, 0])
        XCTAssertFalse(try XCTUnwrap(changed.depthImage).overlay === depth.overlay)
        try Data(
            #"{"frame":"local metres","origin":{"latitude":0,"longitude":0},"cameras":[],"points":"unused"}"#.utf8
        ).write(to: alignment)
        let metric = try await reader.load(root: root, run: run, unit: units[0], mode: "Depth")
        XCTAssertTrue(try XCTUnwrap(metric.depthImage).isMetric)

        for unit in units.dropFirst() {
            _ = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        }
        let evicted = try await reader.load(root: root, run: run, unit: units[0], mode: "Depth")
        XCTAssertFalse(try XCTUnwrap(evicted.depthImage).overlay === metric.depthImage?.overlay)
    }

    func testSavedProjectDepthOpeningTimingsAndSampleParity() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_PROJECT"], let id = environment["ECHO_TEST_RUN"] else {
            throw XCTSkip("Saved reconstruction fixture unavailable")
        }
        let root = URL(fileURLWithPath: path)
        let run = try JSONDecoder().decode(
            RunData.self, from: Data(contentsOf: root.appendingPathComponent("runs/\(id)/run.json")))
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            json: json)
        let units = try await reader.units(root: root, run: run, stage: .depth)
        for unit in units.prefix(3) {
            let start = Date()
            let first = try await reader.load(root: root, run: run, stage: .depth, unit: unit, mode: "Depth", slice: 0)
            let opened = Date()
            let confidence = try await reader.load(
                root: root, run: run, stage: .depth, unit: unit, mode: "Confidence", slice: 0)
            let switched = Date()
            let again = try await reader.load(root: root, run: run, stage: .depth, unit: unit, mode: "Depth", slice: 0)
            print(
                "DEPTH OPEN \(unit.title): first \(opened.timeIntervalSince(start)) s, mode \(switched.timeIntervalSince(opened)) s, cached \(Date().timeIntervalSince(switched)) s"
            )
            let actual = try XCTUnwrap(first.depthImage)
            let expected = try JSONDecoder().decode(DepthMap.self, from: Data(contentsOf: unit.file))
            XCTAssertEqual(actual.map.depth.map(\.bitPattern), expected.depth.map(\.bitPattern))
            XCTAssertEqual(actual.map.confidence.map(\.bitPattern), expected.confidence.map(\.bitPattern))
            XCTAssertNotNil(actual.photograph)
            XCTAssertEqual(confidence.depthImage?.identity, actual.identity)
            XCTAssertTrue(try XCTUnwrap(again.depthImage).overlay === actual.overlay)
        }
    }

    private func writeDepth(_ file: URL, imageID: String, value: Float) throws {
        try JSONSerialization.data(withJSONObject: [
            "imageId": imageID, "width": 2, "height": 1, "depth": [value, 0], "confidence": [0.9, 0],
        ]).write(to: file, options: .atomic)
    }

    private func photo(_ id: String) -> Photo {
        Photo(
            id: id, name: id, source: id + ".jpg", preview: id + ".jpg", width: 2, height: 1,
            capturedAt: nil, camera: "", focalPixels: 1, enabled: true, sets: [], gps: nil, attitude: nil, metadata: [:]
        )
    }
}
