import AppKit
import XCTest

@testable import EchoDesktop

final class SavedArtifactIntegrationTests: XCTestCase {
    @MainActor
    func testNativeReadersOpenEverySavedStage() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let projectPath = environment["ECHO_TEST_PROJECT"],
            let runID = environment["ECHO_TEST_RUN"]
        else {
            throw XCTSkip("Set ECHO_TEST_PROJECT and ECHO_TEST_RUN to inspect a saved reconstruction.")
        }

        let root = URL(fileURLWithPath: projectPath)
        let runFile = root.appendingPathComponent("runs/\(runID)/run.json")
        let run = try JSONDecoder().decode(RunData.self, from: Data(contentsOf: runFile))
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            json: json)
        XCTAssertFalse(run.snapshot.images.isEmpty)

        for stage in PipelineStage.allCases {
            let units = try await reader.units(root: root, run: run, stage: stage)
            if [.features, .matching, .depth].contains(stage) {
                XCTAssertFalse(units.isEmpty)
                XCTAssertTrue(units.allSatisfy { !$0.imageIDs.isEmpty && !$0.thumbnails.isEmpty && !$0.title.isEmpty })
            }
            let presentation = try await reader.load(
                root: root, run: run, stage: stage, unit: units.first,
                mode: stage == .fusion ? "Y" : "Textured", slice: 0.5)
            XCTAssertFalse(presentation.detail.isEmpty, stage.rawValue)
            switch stage {
            case .features:
                let features = try XCTUnwrap(presentation.features)
                XCTAssertFalse(features.features.isEmpty)
                XCTAssertFalse(features.matches.isEmpty)
                XCTAssertTrue(features.matches.allSatisfy { features.features.indices.contains($0.featureIndex) })
            case .matching:
                let matches = try XCTUnwrap(presentation.matches)
                XCTAssertFalse(matches.matches.isEmpty)
                XCTAssertGreaterThan(matches.firstImage.size.width, 0)
                XCTAssertGreaterThan(matches.secondImage.size.width, 0)
            case .alignment, .cloud, .mesh, .texture, .export:
                let scene = try XCTUnwrap(presentation.scene, stage.rawValue)
                XCTAssertFalse(scene.rootNode.childNodes.isEmpty, stage.rawValue)
            case .depth:
                let depth = try XCTUnwrap(presentation.depthImage)
                XCTAssertGreaterThan(depth.overlay.size.width, 0)
                XCTAssertNotNil(depth.photograph)
            default:
                let image = try XCTUnwrap(presentation.image, stage.rawValue)
                XCTAssertGreaterThan(image.size.width, 0, stage.rawValue)
            }
        }

        var previewArtifacts = run.artifacts
        previewArtifacts["cloudPreview"] = previewArtifacts.removeValue(forKey: "cloud")
        let constructing = RunData(
            id: run.id, status: "running", target: "cloud", snapshot: run.snapshot,
            artifacts: previewArtifacts, error: nil, progress: nil)
        let preview = try await reader.load(
            root: root, run: constructing, stage: .cloud,
            unit: nil, mode: "All support", slice: 0.5)
        XCTAssertNotNil(preview.scene)
        XCTAssertTrue(preview.detail.contains("LIVE PREVIEW"))

        let savedFeatureIndex = try XCTUnwrap(previewArtifacts.removeValue(forKey: "features"))
        let featureIndex = try JSONDecoder().decode(
            [String: String].self,
            from: Data(contentsOf: root.appendingPathComponent(savedFeatureIndex).appendingPathComponent("units.json")))
        let imageIDs = featureIndex.keys.sorted()
        XCTAssertGreaterThanOrEqual(imageIDs.count, 2)
        let previewPath = "work/native-feature-preview-\(UUID().uuidString)"
        let previewFolder = root.appendingPathComponent(previewPath)
        defer { try? FileManager.default.removeItem(at: previewFolder) }
        for count in 1...2 {
            let folder = previewFolder.appendingPathComponent("\(count)")
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let index = featureIndex.filter { imageIDs.prefix(count).contains($0.key) }
            try JSONEncoder().encode(index).write(to: folder.appendingPathComponent("units.json"))
        }
        previewArtifacts["featuresPreview"] = previewPath + "/1"
        previewArtifacts.removeValue(forKey: "matching")
        let extracting = RunData(
            id: run.id, status: "running", target: "features", snapshot: run.snapshot,
            artifacts: previewArtifacts, error: nil, progress: nil)
        let featureUnits = try await reader.units(root: root, run: extracting, stage: .features)
        XCTAssertEqual(featureUnits.count, 1)
        let model = ArtifactViewModel(reader: reader)
        await model.configure(root: root.path, run: extracting, stage: .features)
        let selected = try XCTUnwrap(model.presentation)
        XCTAssertNotNil(selected.features)
        XCTAssertFalse(try XCTUnwrap(selected.features).matchingAvailable)
        previewArtifacts["featuresPreview"] = previewPath + "/2"
        let updated = RunData(
            id: run.id, status: "running", target: "features", snapshot: run.snapshot,
            artifacts: previewArtifacts, error: nil, progress: nil)
        await model.configure(root: root.path, run: updated, stage: .features)
        XCTAssertEqual(
            model.presentation?.id, selected.id, "Live index refresh keeps the inspected photo and selection")
        XCTAssertEqual(model.units.count, 2)
        model.selectUnit(imageIDs[1])
        await model.configure(root: root.path, run: updated, stage: .features)
        for _ in 0..<500 {
            if !model.loading { break }
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertFalse(model.loading)
        XCTAssertEqual(
            model.presentation?.features?.photo.id, imageIDs[1], "A live refresh must not cancel switching photos")
    }
}
