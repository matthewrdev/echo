import Foundation
import XCTest

@testable import EchoDesktop

final class RunProgressTests: XCTestCase {
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
