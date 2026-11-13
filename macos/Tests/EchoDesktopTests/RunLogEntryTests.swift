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
}
