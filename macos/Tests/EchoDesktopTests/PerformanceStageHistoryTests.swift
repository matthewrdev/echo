import XCTest

@testable import EchoDesktop

final class PerformanceStageHistoryTests: XCTestCase {
    func testReuseFollowsMatchingArtifactAcrossRunsAndSkipsLaterReuse() async throws {
        let original = try PerformanceTraceFixture.document(id: "original", run: "source", second: 1, duration: 84_000)
        let reuse = try PerformanceTraceFixture.document(
            id: "reuse", run: "source", second: 2, duration: 800, reused: 10)
        let unrelated = try PerformanceTraceFixture.document(id: "other", run: "other", second: 3, duration: 60_000)
        let current = try PerformanceTraceFixture.document(
            id: "current", run: "current", second: 4, duration: 12_000, reused: 10)
        let future = try PerformanceTraceFixture.document(id: "future", run: "source", second: 5, duration: 90_000)
        let cli = TestPerformanceCLIClient()
        cli.documents = [future, current, unrelated, reuse, original]
        cli.artifacts = [
            "source": ["features": "artifacts/features/saved"], "current": ["features": "artifacts/features/saved"],
            "other": ["features": "artifacts/features/different"],
        ]
        let timings = await PerformanceStageHistory(client: CLIProjectClient(cli: cli, traces: cli)).timings(
            for: current, attempts: cli.documents.map(\.summary), root: "/project")
        let timing = try XCTUnwrap(timings["features"])
        XCTAssertEqual(timing.milliseconds, 84_000)
        XCTAssertEqual(timing.gpuMilliseconds, 4500)
        XCTAssertEqual(timing.attempt.id, "original")
        let nodes = PerformanceGraphNode.make(current, originalTimings: timings)
        XCTAssertEqual(nodes[1].status, .reused)
        XCTAssertEqual(nodes[1].durationLabel, "1.40 min original")
        XCTAssertEqual(nodes[1].work, "Metal work · 4.50 s GPU")
        XCTAssertTrue(nodes[1].timingHelp.contains("This attempt: 12.00 s"))
        XCTAssertEqual(nodes.last?.milliseconds, current.summary.durationMilliseconds)
        XCTAssertEqual(current.spans[0].durationMilliseconds, 12_000)
    }

    func testResumeCanUseCompletedStageFromCancelledAttempt() async throws {
        let original = try PerformanceTraceFixture.document(
            id: "original", run: "run", second: 1, duration: 84_000, status: "cancelled")
        let interrupted = try PerformanceTraceFixture.document(
            id: "interrupted", run: "run", second: 2, duration: 7000, stageStatus: "cancelled", status: "cancelled")
        let current = try PerformanceTraceFixture.document(
            id: "current", run: "run", second: 3, duration: 100, reused: 10)
        let cli = TestPerformanceCLIClient()
        cli.documents = [current, interrupted, original]
        cli.artifacts = ["run": ["features": "artifacts/features/saved"]]
        let timings = await PerformanceStageHistory(client: CLIProjectClient(cli: cli, traces: cli)).timings(
            for: current, attempts: cli.documents.map(\.summary), root: "/project")
        XCTAssertEqual(timings["features"]?.milliseconds, 84_000)
    }

    func testMissingHistoryDoesNotPresentReuseTimeAsProcessingTime() async throws {
        let original = try PerformanceTraceFixture.document(id: "original", run: "run", second: 1, duration: 84_000)
        let current = try PerformanceTraceFixture.document(
            id: "current", run: "run", second: 2, duration: 100, reused: 10)
        let cli = TestPerformanceCLIClient()
        cli.documents = [current, original]
        cli.artifacts = ["run": ["features": "artifacts/features/saved"]]
        cli.missingAttempts = ["original"]
        let timings = await PerformanceStageHistory(client: CLIProjectClient(cli: cli, traces: cli)).timings(
            for: current, attempts: cli.documents.map(\.summary), root: "/project")
        XCTAssertTrue(timings.isEmpty)
        let node = PerformanceGraphNode.make(current, originalTimings: timings)[1]
        XCTAssertNil(node.milliseconds)
        XCTAssertEqual(node.durationLabel, "Not recorded")
        XCTAssertEqual(node.work, "Original processing time unavailable")
    }

    func testNewAndPartiallyReusedStagesKeepCurrentTiming() async throws {
        for reused in [0, 5] {
            let current = try PerformanceTraceFixture.document(
                id: "current", run: "run", second: 1, duration: 2500, reused: reused)
            let cli = TestPerformanceCLIClient()
            let timings = await PerformanceStageHistory(client: CLIProjectClient(cli: cli, traces: cli)).timings(
                for: current, attempts: [], root: "/project")
            XCTAssertTrue(cli.requests.isEmpty)
            XCTAssertTrue(timings.isEmpty)
            XCTAssertEqual(PerformanceGraphNode.make(current)[1].durationLabel, "2.50 s")
        }
    }

    @MainActor
    func testViewModelLoadsOriginalTimingAndClearsItWhenSelectingAnotherAttempt() async throws {
        let original = try PerformanceTraceFixture.document(id: "original", run: "run", second: 1, duration: 84_000)
        let current = try PerformanceTraceFixture.document(
            id: "current", run: "run", second: 2, duration: 100, reused: 10)
        let cli = TestPerformanceCLIClient()
        cli.documents = [current, original]
        cli.artifacts = ["run": ["features": "artifacts/features/saved"]]
        let model = PerformanceTraceViewModel(
            client: CLIProjectClient(cli: cli, traces: cli),
            fileDialogLocations: FileDialogLocations(defaults: .standard))
        await model.refresh(root: "/project", preferredRun: "run")
        XCTAssertNil(model.error)
        XCTAssertEqual(model.originalTimings["features"]?.milliseconds, 84_000)
        let requestCount = cli.requests.count
        await model.load()
        XCTAssertEqual(cli.requests.count, requestCount + 1, "Refreshing the trace should reuse resolved history")
        model.selected = "original"
        await model.load()
        XCTAssertTrue(model.originalTimings.isEmpty)
        XCTAssertEqual(model.document?.summary.id, "original")
    }
}
