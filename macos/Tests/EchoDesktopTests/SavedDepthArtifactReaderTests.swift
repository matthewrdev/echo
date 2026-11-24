import AppKit
import EchoRendering
import SwiftUI
import XCTest

@testable import EchoDesktop

final class SavedDepthArtifactReaderTests: XCTestCase {
    func testModesAndRecentPhotosReuseImagesButChangedFilesInvalidateThem() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let alignment = root.appendingPathComponent("scene.json")
        try BinaryArtifactFixture.write(alignment, value: ["frame": "arbitrary", "cameras": []])
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
        try BinaryArtifactFixture.write(
            alignment, value: ["frame": "local metres", "origin": ["latitude": 0, "longitude": 0], "cameras": []])
        let metric = try await reader.load(root: root, run: run, unit: units[0], mode: "Depth")
        XCTAssertTrue(try XCTUnwrap(metric.depthImage).isMetric)

        for unit in units.dropFirst() {
            _ = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        }
        let evicted = try await reader.load(root: root, run: run, unit: units[0], mode: "Depth")
        XCTAssertFalse(try XCTUnwrap(evicted.depthImage).overlay === metric.depthImage?.overlay)
    }

    func testSavedProjectDepthOpeningTimingsAndSampleParity() async throws {
        try WindowedTests.requireOptIn()

        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_PROJECT"], let id = environment["ECHO_TEST_RUN"] else {
            throw XCTSkip("Saved reconstruction fixture unavailable")
        }
        let root = URL(fileURLWithPath: path)
        let run = try await SavedRunFixture.read(root, id: id)
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
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
            let expected: DepthMap = try BulkArtifactReader().read(unit.file, metadata: Data(contentsOf: unit.file))
            XCTAssertEqual(actual.map.depth.map(\.bitPattern), expected.depth.map(\.bitPattern))
            XCTAssertEqual(actual.map.confidence.map(\.bitPattern), expected.confidence.map(\.bitPattern))
            XCTAssertNotNil(actual.photograph)
            XCTAssertEqual(confidence.depthImage?.identity, actual.identity)
            XCTAssertTrue(try XCTUnwrap(again.depthImage).overlay === actual.overlay)
            if run.artifacts["semantics"] != nil {
                XCTAssertTrue(actual.quality.hasSkyMask)
                XCTAssertTrue((0...1).contains(try XCTUnwrap(actual.quality.surfaceCoverage)))
            }
            if let output = environment["ECHO_TEST_RENDER_DIR"] {
                try await render(first, to: URL(fileURLWithPath: output).appendingPathComponent(unit.id + ".png"))
            }
        }
    }

    @MainActor
    private func render(_ presentation: ArtifactPresentation, to file: URL) async throws {
        let depth = try XCTUnwrap(presentation.depthImage)
        let view = NSHostingView(
            rootView: VStack(spacing: 12) {
                HStack(spacing: 12) {
                    Text(presentation.title).fontWeight(.semibold)
                    Spacer()
                    DepthQualityBadge(summary: depth.quality)
                    Text(presentation.detail).foregroundStyle(.secondary)
                }.font(.caption)
                DepthImageViewport(data: depth, mode: .constant("Depth"))
            }.padding(20).frame(width: 1200, height: 760).background(Color.white).environment(\.colorScheme, .light))
        view.frame = NSRect(x: 0, y: 0, width: 1200, height: 760)
        let window = NSWindow(contentRect: view.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = view
        defer { window.contentView = nil }
        view.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(150))
        view.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: file)
        print("DEPTH COVERAGE \(presentation.title): \(depth.quality.grade.rawValue), \(presentation.detail)")
    }

    func testSavedSkyMasksUpdateCoverageAndInvalidateCachedPresentations() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let file = root.appendingPathComponent("depth.json")
        try writeDepth(file, imageID: "photo", value: 10)
        let unit = ArtifactUnit(
            id: "photo", title: "Photo", file: file, imageIDs: ["photo"], thumbnails: [], detail: "")
        let run = RunData(
            id: "run", status: "completed", target: "depth",
            snapshot: ProjectData(
                id: "project", name: "Test", revision: 1, images: [photo("photo")], settings: ProcessingSettings(),
                hints: []),
            artifacts: ["semantics": "."], error: nil, progress: nil)
        let reader = SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer())
        let missing = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        XCTAssertFalse(try XCTUnwrap(missing.depthImage).quality.hasSkyMask)
        XCTAssertTrue(missing.detail.contains("whole-image coverage"))
        let index = root.appendingPathComponent("units.json")
        try JSONSerialization.data(withJSONObject: ["photo": "."]).write(to: index)
        let mask = root.appendingPathComponent("semantics.json")
        try BinaryArtifactFixture.write(
            mask, value: ["imageId": "photo", "width": 2, "height": 1, "sky": [0, 255]])
        let surface = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        let quality = try XCTUnwrap(surface.depthImage).quality
        XCTAssertEqual(quality.grade, .dense)
        XCTAssertEqual(quality.coverage, 0.5)
        XCTAssertEqual(quality.surfaceCoverage, 1)
        XCTAssertTrue(surface.detail.contains("surface coverage"))
        XCTAssertFalse(try XCTUnwrap(surface.depthImage).overlay === missing.depthImage?.overlay)
        let confidence = try await reader.load(root: root, run: run, unit: unit, mode: "Confidence")
        XCTAssertEqual(confidence.depthImage?.quality.surfaceCoverage, quality.surfaceCoverage)
        let cached = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        XCTAssertTrue(try XCTUnwrap(cached.depthImage).overlay === surface.depthImage?.overlay)

        try BinaryArtifactFixture.write(
            mask, value: ["imageId": "photo", "width": 2, "height": 1, "sky": [0, 203]])
        let changed = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        XCTAssertEqual(changed.depthImage?.quality.grade, .broad)
        XCTAssertEqual(changed.depthImage?.quality.excludedSkyPixels, 0)

        try BinaryArtifactFixture.write(
            mask, value: ["imageId": "different", "width": 2, "height": 1, "sky": [0, 255]])
        let mismatched = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        XCTAssertFalse(try XCTUnwrap(mismatched.depthImage).quality.hasSkyMask)
        try Data("invalid mask".utf8).write(to: mask, options: .atomic)
        let invalid = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        XCTAssertFalse(try XCTUnwrap(invalid.depthImage).quality.hasSkyMask)
        try JSONSerialization.data(withJSONObject: ["photo": "missing"]).write(to: index, options: .atomic)
        let moved = try await reader.load(root: root, run: run, unit: unit, mode: "Depth")
        XCTAssertFalse(try XCTUnwrap(moved.depthImage).quality.hasSkyMask)
    }

    private func writeDepth(_ file: URL, imageID: String, value: Float) throws {
        try BinaryArtifactFixture.write(
            file,
            value: [
                "imageId": imageID, "width": 2, "height": 1, "depth": [value, 0], "confidence": [0.9, 0],
            ])
    }

    private func photo(_ id: String) -> Photo {
        Photo(
            id: id, name: id, source: id + ".jpg", preview: id + ".jpg", width: 2, height: 1,
            capturedAt: nil, camera: "", focalPixels: 1, enabled: true, sets: [], gps: nil, attitude: nil, metadata: [:]
        )
    }
}
