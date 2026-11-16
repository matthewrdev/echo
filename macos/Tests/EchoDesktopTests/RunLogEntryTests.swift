import Foundation
import Testing

@testable import EchoDesktop

struct RunLogEntryTests {
    @Test func timestampsKeepMilliseconds() {
        let precise = RunLogEntry(
            id: 1, timestamp: "2026-11-13T08:37:19.1234567+00:00", stage: "depth", message: "Test")
        #expect(precise.time.contains("123"))
        let wholeSecond = RunLogEntry(id: 2, timestamp: "2026-11-13T08:37:19Z", stage: "depth", message: "Test")
        #expect(wholeSecond.time.contains("000"))
        #expect(!wholeSecond.time.isEmpty)
    }

    @Test func timestampsAcceptOffsetsAndRejectInvalidDates() {
        let utc = RunLogEntry(id: 1, timestamp: "2026-11-16T09:35:07.123Z", stage: "mesh", message: "Test")
        let local = RunLogEntry(id: 2, timestamp: "2026-11-16T20:35:07.123+11:00", stage: "mesh", message: "Test")
        #expect(utc.time == local.time)
        #expect(RunLogEntry(id: 3, timestamp: "invalid", stage: "mesh", message: "Test").time.isEmpty)
    }
}
