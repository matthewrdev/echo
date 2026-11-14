import XCTest

@testable import EchoDesktop

final class DepthQualityTests: XCTestCase {
    func testSparseHighConfidenceMapStillHasPoorHealth() {
        var depth = [Float](repeating: 0, count: 4_800)
        depth[0] = 100
        let summary = DepthQualitySummary(
            map: DepthMap(
                imageId: "sparse", width: 80, height: 60, depth: depth,
                confidence: [Float](repeating: 0.99, count: depth.count)))
        XCTAssertEqual(summary.grade, .poor)
        XCTAssertEqual(summary.accepted, 1)
        XCTAssertEqual(summary.meanConfidence ?? 0, 0.99, accuracy: 0.0001)
        XCTAssertEqual(summary.coveredRegions, 0)
        XCTAssertEqual(summary.regionCount, 48)
    }

    func testCoverageBandsAndUnknownSamples() {
        for count in [0, 4, 5, 24, 25, 49, 50, 100] {
            let depth = [Float](repeating: 10, count: count) + [Float](repeating: 0, count: 100 - count)
            let summary = DepthQualitySummary(
                map: DepthMap(imageId: "coverage", width: 10, height: 10, depth: depth, confidence: []))
            XCTAssertEqual(summary.accepted, count)
            XCTAssertEqual(summary.grade, DepthHealthGrade(coverage: Double(count) / 100))
            XCTAssertNil(summary.meanConfidence)
        }
        XCTAssertEqual(DepthHealthGrade(coverage: 0), .empty)
        XCTAssertEqual(DepthHealthGrade(coverage: 0.049), .poor)
        XCTAssertEqual(DepthHealthGrade(coverage: 0.05), .limited)
        XCTAssertEqual(DepthHealthGrade(coverage: 0.25), .partial)
        XCTAssertEqual(DepthHealthGrade(coverage: 0.5), .broad)
        let invalid = DepthQualitySummary(
            map: DepthMap(
                imageId: "invalid", width: 4, height: 1, depth: [.nan, .infinity, -1, 10], confidence: [1, 1, 1, .nan]))
        XCTAssertEqual(invalid.accepted, 1)
        XCTAssertNil(invalid.meanConfidence)
        let malformed = DepthQualitySummary(
            map: DepthMap(imageId: "invalid", width: Int.max, height: 3, depth: [], confidence: []))
        XCTAssertEqual(malformed.total, 0)
        XCTAssertEqual(malformed.grade, .empty)
    }
}
