import XCTest

@testable import EchoDesktop

final class SavedArtifactPresentationTests: XCTestCase {
    func testRetainsCompatibleResultsWithoutChangingTheProcessorRun() throws {
        let saved = try run(
            id: "saved", status: "completed", artifacts: ["features": "features/old", "depth": "depth/old"])
        let current = try run(id: "new", status: "running")
        let displayed = SavedArtifactPresentation(current: current, candidates: [saved])
        XCTAssertTrue(current.artifacts.isEmpty)
        XCTAssertEqual(displayed.run.artifacts["features"], "features/old")
        XCTAssertEqual(displayed.retainedStages, [.features, .depth])
        let finished = try run(id: "new", status: "completed")
        XCTAssertEqual(
            SavedArtifactPresentation(current: finished, candidates: [saved]).retainedStages, [.features, .depth])
    }

    func testChangedInputsAndLivePreviewsTakePrecedence() throws {
        let saved = try run(
            id: "saved", status: "completed", artifacts: ["features": "features/old", "depth": "depth/old"])
        let changed = try run(id: "new", status: "running", settings: ["depthWidth": 2048])
        let displayed = SavedArtifactPresentation(current: changed, candidates: [saved])
        XCTAssertEqual(displayed.retainedStages, [.features])
        let preview = try run(id: "new", status: "running", artifacts: ["featuresPreview": "preview/new"])
        XCTAssertNil(SavedArtifactPresentation(current: preview, candidates: [saved]).run.artifacts["features"])
        let reset = try run(id: "new", status: "running", revisions: ["features": "reset"])
        XCTAssertTrue(SavedArtifactPresentation(current: reset, candidates: [saved]).run.artifacts.isEmpty)
    }

    func testCropRunKeepsTheFullCloudVisibleWhileFusionIsRebuilt() throws {
        let saved = try run(
            id: "saved", status: "completed",
            artifacts: [
                "alignment": "alignment/old", "cloud": "cloud/full", "fusion": "fusion/old", "texture": "texture/old",
            ])
        let cropped = try run(
            id: "cropped", status: "running",
            settings: [
                "exclusionRegions": [
                    [
                        "id": "crop", "enabled": true, "shape": "box", "mode": "include",
                        "centreX": 0, "centreY": 0, "centreZ": 0, "width": 1, "height": 1, "depth": 1, "radius": 0.5,
                    ]
                ]
            ])
        let displayed = SavedArtifactPresentation(current: cropped, candidates: [saved])
        XCTAssertEqual(displayed.run.artifacts["cloud"], "cloud/full")
        XCTAssertEqual(displayed.run.artifacts["alignment"], "alignment/old")
        XCTAssertNil(displayed.run.artifacts["fusion"])
        XCTAssertNil(displayed.run.artifacts["texture"])
        XCTAssertEqual(displayed.retainedStages, [.alignment, .cloud])
    }

    func testStoppedAttemptsRetainCompletedResultsOverPartialPreviews() throws {
        let saved = try run(
            id: "saved", status: "completed", artifacts: ["features": "features/old", "depth": "depth/old"])
        for status in ["cancelled", "failed", "needs-input"] {
            let stopped = try run(id: "stopped", status: status, artifacts: ["featuresPreview": "preview/partial"])
            let displayed = SavedArtifactPresentation(current: stopped, candidates: [saved])
            XCTAssertEqual(displayed.retainedStages, [.features, .depth], status)
            XCTAssertEqual(displayed.sourceRuns[.depth]?.id, saved.id)
            XCTAssertNil(stopped.artifacts["depth"], "Presentation must not rewrite the attempt's record")
            let changed = try run(id: "changed", status: status, settings: ["depthWidth": 2048])
            XCTAssertEqual(SavedArtifactPresentation(current: changed, candidates: [saved]).retainedStages, [.features])
        }
    }

    func testChangedUpstreamArtifactsAndMissingSavedFilesAreNotRestored() throws {
        let saved = try run(
            id: "saved", status: "completed", artifacts: ["features": "features/old", "depth": "depth/old"])
        let changed = try run(id: "new", status: "cancelled", artifacts: ["features": "features/new-algorithm"])
        XCTAssertTrue(SavedArtifactPresentation(current: changed, candidates: [saved]).retainedStages.isEmpty)
        let stopped = try run(id: "stopped", status: "cancelled")
        XCTAssertTrue(
            SavedArtifactPresentation(current: stopped, candidates: [saved], artifactExists: { _ in false })
                .retainedStages.isEmpty)
    }

    private func run(
        id: String, status: String, artifacts: [String: String] = [:], settings: [String: Any] = [:],
        revisions: [String: String] = [:]
    ) throws -> RunData {
        let object: [String: Any] = [
            "id": id, "status": status, "target": "cloud", "artifacts": artifacts,
            "snapshot": [
                "id": "project", "name": "Project", "revision": 1, "images": [],
                "settings": settings, "hints": [], "stageRevisions": revisions,
            ],
        ]
        return try JSONDecoder().decode(RunData.self, from: JSONSerialization.data(withJSONObject: object))
    }
}
