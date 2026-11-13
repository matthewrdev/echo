import AppKit
import XCTest

@testable import EchoDesktop

final class SavedArtifactIntegrationTests: XCTestCase {
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
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer())
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
    }
}
