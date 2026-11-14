import XCTest

@testable import EchoDesktop

final class ArtifactLoadingTests: XCTestCase {
    @MainActor
    func testCancelDismissesLoadingImmediatelyAndIgnoresLateResults() async throws {
        let reader = TestDeferredArtifactReader()
        let model = ArtifactViewModel(reader: reader)
        let run = RunData(
            id: "run", status: "completed", target: "alignment",
            snapshot: ProjectData(
                id: "project", name: "Test", revision: 1, images: [], settings: ProcessingSettings(), hints: []),
            artifacts: ["alignment": "alignment"], error: nil, progress: nil)
        let task = Task { await model.configure(root: "/test", run: run, stage: .alignment) }
        while !(await reader.started) { await Task.yield() }
        XCTAssertTrue(model.loading)
        model.cancelLoading()
        XCTAssertFalse(model.loading)
        await reader.finish()
        await task.value
        XCTAssertNil(model.presentation)
        XCTAssertNil(model.error)
    }
}
