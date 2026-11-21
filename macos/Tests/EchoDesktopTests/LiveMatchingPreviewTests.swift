import AppKit
import EchoRendering
import XCTest

@testable import EchoDesktop

final class LiveMatchingPreviewTests: XCTestCase {
    @MainActor
    func testGrowingPairIndexPreservesInspectedPairAndSelectionThroughCompletion() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let image = NSImage(size: CGSize(width: 100, height: 100))
        image.lockFocus()
        NSColor.gray.setFill()
        NSRect(x: 0, y: 0, width: 100, height: 100).fill()
        image.unlockFocus()
        try XCTUnwrap(image.tiffRepresentation).write(to: root.appendingPathComponent("photo.tiff"))
        let photos = (0..<3).map {
            Photo(
                id: "\($0)", name: "Photo \($0)", source: "photo.tiff", preview: "photo.tiff", width: 100,
                height: 100, capturedAt: nil, camera: "", focalPixels: 100, enabled: true, sets: [], gps: nil,
                attitude: nil, metadata: [:])
        }
        let fixture = MatchingArtifactFixture()
        var entries: [MatchingIndexEntry] = []
        let preview = root.appendingPathComponent("preview")
        try FileManager.default.createDirectory(at: preview, withIntermediateDirectories: true)
        let journal = preview.appendingPathComponent("pairs.ndjson")
        for index in 1...2 {
            let artifact = "artifacts/match-pair/pair-\(index)"
            try fixture.writePair(root: root, artifact: artifact, a: "0", b: "\(index)")
            entries.append(
                MatchingIndexEntry(
                    sequence: index - 1, artifact: artifact, a: "0", b: "\(index)", pointCount: 1, lineCount: 0))
        }
        var firstEntry = try JSONEncoder().encode(entries[0])
        firstEntry.append(10)
        try firstEntry.write(to: journal)
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let model = ArtifactViewModel(reader: reader)
        let snapshot = ProjectData(
            id: "project", name: "Test", revision: 1, images: photos, settings: ProcessingSettings(), hints: [])
        func run(_ artifacts: [String: String], status: String = "running") -> RunData {
            RunData(
                id: "run", status: status, target: "matching", snapshot: snapshot, artifacts: artifacts,
                error: nil, progress: nil)
        }
        await model.configure(root: root.path, run: run([:]), stage: .matching)
        XCTAssertTrue(model.units.isEmpty)
        await model.configure(root: root.path, run: run(["matchingPreview": "preview"]), stage: .matching)
        XCTAssertNil(model.error)
        XCTAssertEqual(model.units.count, 1)
        XCTAssertEqual(model.presentation?.matches?.second.id, "1")
        model.selectedMatchIndex = 0
        let first = try XCTUnwrap(model.presentation?.id)
        let expanded = run(["matchingPreview": "preview"])
        let writer = try FileHandle(forWritingTo: journal)
        defer { try? writer.close() }
        try writer.seekToEnd()
        let secondEntry = try JSONEncoder().encode(entries[1])
        try writer.write(contentsOf: secondEntry.prefix(secondEntry.count / 2))
        await model.configure(root: root.path, run: expanded, stage: .matching)
        XCTAssertNil(model.error)
        XCTAssertEqual(model.units.count, 1)
        try writer.write(contentsOf: secondEntry.suffix(secondEntry.count - secondEntry.count / 2))
        try writer.write(contentsOf: Data([10]))
        await model.configure(root: root.path, run: expanded, stage: .matching)
        XCTAssertEqual(model.units.count, 2)
        XCTAssertEqual(model.presentation?.id, first)
        XCTAssertEqual(model.selectedMatchIndex, 0)
        model.selectUnit("artifacts/match-pair/pair-2")
        await model.configure(root: root.path, run: expanded, stage: .matching)
        for _ in 0..<100 {
            if !model.loading, model.presentation?.matches?.second.id == "2" { break }
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertEqual(model.presentation?.matches?.second.id, "2")
        model.selectedMatchIndex = 0
        let second = try XCTUnwrap(model.presentation?.id)
        await model.configure(
            root: root.path, run: run(["matching": "preview"], status: "completed"), stage: .matching)
        XCTAssertEqual(model.presentation?.id, second)
        XCTAssertEqual(model.selectedMatchIndex, 0)
        XCTAssertNil(model.error)
    }
}
