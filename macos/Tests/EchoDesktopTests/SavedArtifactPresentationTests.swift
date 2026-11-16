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
        XCTAssertTrue(SavedArtifactPresentation(current: finished, candidates: [saved]).run.artifacts.isEmpty)
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
