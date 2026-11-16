import Foundation
import XCTest

@testable import EchoDesktop

final class ProjectInspectionTests: XCTestCase {
    func testInspectionDecodesArtifactWarningsAndOlderResponses() throws {
        let project = String(decoding: TestProjectManagementCLIClient().response, as: UTF8.self)
        let json = "{\"projectRoot\":\"/saved.echo\",\"project\":\(project)"
        let old = try JSONDecoder().decode(Inspection.self, from: Data((json + "}").utf8))
        XCTAssertNil(old.warnings)
        let current = try JSONDecoder().decode(
            Inspection.self, from: Data((json + ",\"warnings\":[\"Saved alignment could not be read\"]}").utf8))
        XCTAssertEqual(current.warnings, ["Saved alignment could not be read"])
        XCTAssertEqual(current.project.id, "saved-project")
    }
}
