import AppKit
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
        for index in 1...2 {
            let folder = root.appendingPathComponent("pair-\(index)")
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try JSONSerialization.data(withJSONObject: [
                "a": "0", "b": "\(index)", "matches": [["a": [20, 20], "b": [30, 30], "manual": false]],
            ]).write(to: folder.appendingPathComponent("pair.json"))
            let preview = root.appendingPathComponent("preview-\(index)")
            try FileManager.default.createDirectory(at: preview, withIntermediateDirectories: true)
            try JSONEncoder().encode((1...index).map { "pair-\($0)" }).write(
                to: preview.appendingPathComponent("units.json"))
        }
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
        await model.configure(root: root.path, run: run(["matchingPreview": "preview-1"]), stage: .matching)
        XCTAssertNil(model.error)
        XCTAssertEqual(model.units.count, 1)
        XCTAssertEqual(model.presentation?.matches?.second.id, "1")
        model.selectedMatchIndex = 0
        let first = try XCTUnwrap(model.presentation?.id)
        let expanded = run(["matchingPreview": "preview-2"])
        await model.configure(root: root.path, run: expanded, stage: .matching)
        XCTAssertEqual(model.units.count, 2)
        XCTAssertEqual(model.presentation?.id, first)
        XCTAssertEqual(model.selectedMatchIndex, 0)
        model.selectUnit("pair-2")
        await model.configure(root: root.path, run: expanded, stage: .matching)
        for _ in 0..<100 {
            if !model.loading, model.presentation?.matches?.second.id == "2" { break }
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertEqual(model.presentation?.matches?.second.id, "2")
        model.selectedMatchIndex = 0
        let second = try XCTUnwrap(model.presentation?.id)
        await model.configure(
            root: root.path, run: run(["matching": "preview-2"], status: "completed"), stage: .matching)
        XCTAssertEqual(model.presentation?.id, second)
        XCTAssertEqual(model.selectedMatchIndex, 0)
        XCTAssertNil(model.error)
    }
}
