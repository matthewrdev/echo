import XCTest

@testable import EchoDesktop

final class CloudDisplayDetailTests: XCTestCase {
    func testLargeSavedCloudCannotProduceAnUnboundedDraw() {
        let savedCount = 244_617_410
        XCTAssertEqual(CloudDisplayDetail.automatic.limit(for: savedCount), 1_000_000)
        XCTAssertEqual(CloudDisplayDetail.percentage(74).limit(for: savedCount), 4_000_000)
        XCTAssertEqual(CloudDisplayDetail.percentage(100).limit(for: savedCount), 4_000_000)
        XCTAssertEqual(CloudDisplayDetail.pointLimit(Int.max).limit(for: savedCount), 4_000_000)
        XCTAssertEqual(CloudDisplayDetail.percentage(100).limit(for: Int.max), 4_000_000)
    }

    func testSmallCloudsRemainCompleteAndManualDetailStillSamples() {
        XCTAssertEqual(CloudDisplayDetail.automatic.limit(for: 10_000), 10_000)
        XCTAssertEqual(CloudDisplayDetail.pointLimit(500).limit(for: 10_000), 500)
        XCTAssertEqual(CloudDisplayDetail.percentage(25).limit(for: 101), 25)
        XCTAssertEqual(CloudDisplayDetail.automatic.limit(for: 0), 0)
    }
}
