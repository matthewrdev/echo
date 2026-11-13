import AppKit
import XCTest

@testable import EchoDesktop

@MainActor
final class ApplicationTerminationTests: XCTestCase {
    func testIdleQuitDoesNotPrompt() {
        let work = TestApplicationWork()
        work.isWorking = false
        let confirmation = TestQuitConfirmation()
        let coordinator = ApplicationTerminationCoordinator(work: work, confirmation: confirmation)
        XCTAssertEqual(coordinator.requestTermination { _ in XCTFail("Idle quit must be immediate") }, .terminateNow)
        XCTAssertEqual(confirmation.prompts, 0)
    }

    func testKeepProcessingDoesNotCancelWork() {
        let work = TestApplicationWork()
        let confirmation = TestQuitConfirmation()
        let coordinator = ApplicationTerminationCoordinator(work: work, confirmation: confirmation)
        XCTAssertEqual(
            coordinator.requestTermination { _ in XCTFail("Cancelled quit must not reply later") }, .terminateCancel)
        XCTAssertEqual(confirmation.prompts, 1)
        XCTAssertEqual(work.stopCount, 0)
        XCTAssertTrue(work.isWorking)
        let alert = NativeQuitConfirmation().makeAlert()
        XCTAssertEqual(alert.buttons.first?.title, "Keep Processing")
        XCTAssertEqual(alert.buttons.first?.keyEquivalent, "\r")
        XCTAssertEqual(alert.buttons.last?.keyEquivalent, "")
    }

    func testConfirmedQuitWaitsForWorkAndIgnoresRepeatedQuit() async {
        let work = TestApplicationWork()
        let confirmation = TestQuitConfirmation()
        confirmation.approved = true
        let coordinator = ApplicationTerminationCoordinator(work: work, confirmation: confirmation)
        let started = expectation(description: "Cancellation started")
        work.started = { started.fulfill() }
        let replied = expectation(description: "Quit allowed only after shutdown")
        var allowed: Bool?
        XCTAssertEqual(
            coordinator.requestTermination { value in
                allowed = value
                replied.fulfill()
            }, .terminateLater)
        await fulfillment(of: [started], timeout: 2)
        XCTAssertNil(allowed)
        XCTAssertEqual(
            coordinator.requestTermination { _ in XCTFail("Must retain the original reply") }, .terminateLater)
        XCTAssertEqual(confirmation.prompts, 1)
        XCTAssertEqual(work.stopCount, 1)
        work.finish()
        await fulfillment(of: [replied], timeout: 2)
        XCTAssertEqual(allowed, true)
    }

    func testShutdownFailureKeepsTheAppOpen() async {
        let work = TestApplicationWork()
        let confirmation = TestQuitConfirmation()
        confirmation.approved = true
        let coordinator = ApplicationTerminationCoordinator(work: work, confirmation: confirmation)
        let started = expectation(description: "Cancellation started")
        work.started = { started.fulfill() }
        let replied = expectation(description: "Quit cancelled")
        XCTAssertEqual(
            coordinator.requestTermination { allowed in
                XCTAssertFalse(allowed)
                replied.fulfill()
            }, .terminateLater)
        await fulfillment(of: [started], timeout: 2)
        work.finish(error: CLIError(message: "Project unavailable"))
        await fulfillment(of: [replied], timeout: 2)
        XCTAssertEqual(confirmation.failures, 1)
        confirmation.approved = false
        XCTAssertEqual(coordinator.requestTermination { _ in }, .terminateCancel)
    }
}
