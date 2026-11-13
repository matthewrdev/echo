import Foundation
import XCTest

@testable import EchoDesktop

final class LiveDepthPreviewTests: XCTestCase {
    @MainActor
    func testCompletedDepthPhotosAppearOutOfOrderWithoutResettingTheOverlay() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_PROJECT"], let id = environment["ECHO_TEST_RUN"] else {
            throw XCTSkip("Saved reconstruction fixture unavailable")
        }
        let root = URL(fileURLWithPath: path)
        let run = try JSONDecoder().decode(
            RunData.self, from: Data(contentsOf: root.appendingPathComponent("runs/\(id)/run.json")))
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer())
        let saved = try await reader.units(root: root, run: run, stage: .depth)
        XCTAssertGreaterThanOrEqual(saved.count, 2)
        let completed = Array(saved.reversed().prefix(2))
        let previewPath = "work/native-depth-preview-\(UUID().uuidString)"
        defer { try? FileManager.default.removeItem(at: root.appendingPathComponent(previewPath)) }
        let first = try preview(root: root, run: run, path: previewPath + "/1", units: [completed[0]])
        let model = ArtifactViewModel(reader: reader)
        await model.configure(root: root.path, run: first, stage: .depth)
        XCTAssertEqual(model.units.count, 1)
        XCTAssertEqual(model.units.first?.imageIDs, completed[0].imageIDs)
        XCTAssertEqual(model.presentation?.depthImage?.identity, completed[0].file.path)
        XCTAssertNotNil(model.presentation?.depthImage?.photograph)
        model.selectMode("Confidence")
        await model.configure(root: root.path, run: first, stage: .depth)
        await waitForLoad(model)
        let selected = try XCTUnwrap(model.presentation)
        let second = try preview(root: root, run: run, path: previewPath + "/2", units: completed)
        await model.configure(root: root.path, run: second, stage: .depth)
        XCTAssertEqual(model.units.count, 2)
        XCTAssertEqual(model.mode, "Confidence")
        XCTAssertEqual(model.presentation?.id, selected.id, "New maps must not reset the inspected overlay")
        model.selectUnit(completed[1].id)
        await model.configure(root: root.path, run: second, stage: .depth)
        await waitForLoad(model)
        XCTAssertEqual(model.presentation?.depthImage?.identity, completed[1].file.path)
        XCTAssertEqual(model.presentation?.title, completed[1].title)
    }

    private func preview(root: URL, run: RunData, path: String, units: [ArtifactUnit]) throws -> RunData {
        let folder = root.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let index = Dictionary(uniqueKeysWithValues: units.map { ($0.imageIDs[0], $0.id) })
        try JSONEncoder().encode(index).write(to: folder.appendingPathComponent("units.json"))
        var artifacts = run.artifacts
        artifacts.removeValue(forKey: "depth")
        artifacts["depthPreview"] = path
        return RunData(
            id: run.id, status: "running", target: "depth", snapshot: run.snapshot,
            artifacts: artifacts, error: nil, progress: nil)
    }

    @MainActor
    private func waitForLoad(_ model: ArtifactViewModel) async {
        for _ in 0..<500 {
            if !model.loading { break }
            try? await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertFalse(model.loading)
        XCTAssertNil(model.error)
    }
}
