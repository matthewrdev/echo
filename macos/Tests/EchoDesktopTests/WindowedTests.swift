import Foundation
import XCTest

enum WindowedTests {
    static var isEnabled: Bool {
        ProcessInfo.processInfo.environment["ECHO_TEST_WINDOWS"] == "1"
    }

    static func requireOptIn() throws {
        try XCTSkipUnless(
            isEnabled,
            "Set ECHO_TEST_WINDOWS=1 to run tests that activate AppKit or create native windows."
        )
    }
}
