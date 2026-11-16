import Foundation
import XCTest

@testable import EchoDesktop

final class ArtifactDeletionTests: XCTestCase {
    func testCleanupArgumentsCannotBecomePurgeAllWithEmptyRetention() {
        XCTAssertEqual(ArtifactDeletionSelection.unused(retaining: []).arguments, ["--unused", "--keep-runs", ""])
        XCTAssertEqual(
            ArtifactDeletionSelection.unused(retaining: ["b", "a"]).arguments, ["--unused", "--keep-runs", "a,b"])
        XCTAssertEqual(
            ArtifactDeletionSelection.artifact("artifacts/mesh/result").arguments,
            ["--artifact", "artifacts/mesh/result"])
        XCTAssertTrue(ArtifactDeletionSelection.allGenerated.arguments.isEmpty)
    }

    func testCleanupPreviewDecodesCLIContractAndExplainsRetention() throws {
        for timestamp in [
            "2026-11-16T10:00:00+00:00", "2026-11-16T10:00:00.1234567+00:00", "9999-12-31T23:59:59.9999999+00:00",
        ] {
            let data = Data(
                """
                {"token":"reviewed","kind":"unused","bytes":1024,"paths":["runs/old","artifacts/mesh/orphan"],"affectedRuns":{},"retainedRuns":["latest","support"],"removedRuns":["old"],"logCutoff":"\(timestamp)"}
                """.utf8)
            let plan = try JSONDecoder().decode(ArtifactDeletionPlan.self, from: data)
            XCTAssertEqual(plan.kind, .unused)
            XCTAssertNotNil(try plan.logCutoffDate)
            XCTAssertTrue(plan.warning.contains("1 older saved runs"))
            XCTAssertTrue(plan.warning.contains("1 supporting runs"))
            XCTAssertTrue(plan.warning.contains("resumable checkpoints are kept"))
        }
    }
}
