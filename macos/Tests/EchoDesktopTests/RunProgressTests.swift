import Foundation
import XCTest

@testable import EchoDesktop

final class RunProgressTests: XCTestCase {
    func testClassificationCompletionDoesNotCompleteDepth() throws {
        let data = Data(
            #"{"stage":"depth","message":"classification complete","steps":["Classification","Stereo depth","Cliff recovery","Save"],"stepIndex":0,"completed":190,"total":190}"#
                .utf8)
        let progress = try JSONDecoder().decode(RunProgress.self, from: data)
        XCTAssertEqual(progress.fractionCompleted, 1)
        XCTAssertEqual(progress.overallFractionCompleted, 0.25)
        XCTAssertEqual(progress.fractionForStep(0), 1)
        XCTAssertEqual(progress.fractionForStep(1), 0)
        XCTAssertEqual(progress.stepSummary, "Classification · step 1 of 4")
    }

    func testProgressIncludesOnlyEnabledStepsAndFinishesAfterSaving() throws {
        let data = Data(
            #"{"stage":"depth","message":"working","steps":["Stereo depth","Save"],"stepIndex":0,"completed":5,"total":10}"#
                .utf8)
        var progress = try JSONDecoder().decode(RunProgress.self, from: data)
        XCTAssertEqual(progress.overallFractionCompleted, 0.25)
        progress.stepIndex = 1
        XCTAssertEqual(progress.fractionForStep(0), 1)
        let saved = Data(
            #"{"stage":"depth","message":"saved","steps":["Stereo depth","Save"],"stepIndex":1,"completed":1,"total":1}"#
                .utf8)
        XCTAssertEqual(try JSONDecoder().decode(RunProgress.self, from: saved).overallFractionCompleted, 1)
        progress.stepIndex = 20
        XCTAssertNil(progress.activeStepIndex)
    }

    func testProgressReadsDotNetTimestampAndOldArtifacts() throws {
        let current = Data(
            #"{"stage":"depth","message":"level 3/5: 200/300 rows","completed":7,"total":14,"updated":"2026-11-13T08:00:00.1234567+00:00"}"#
                .utf8)
        let progress = try JSONDecoder().decode(RunProgress.self, from: current)
        XCTAssertNotNil(progress.updatedAt)
        XCTAssertEqual(progress.completed, 7)
        let old = Data(#"{"stage":"depth","message":"working"}"#.utf8)
        XCTAssertNil(try JSONDecoder().decode(RunProgress.self, from: old).updatedAt)
        let wholeSeconds = Data(#"{"stage":"depth","message":"working","updated":"2026-11-13T08:00:00Z"}"#.utf8)
        XCTAssertNotNil(try JSONDecoder().decode(RunProgress.self, from: wholeSeconds).updatedAt)
    }
}
