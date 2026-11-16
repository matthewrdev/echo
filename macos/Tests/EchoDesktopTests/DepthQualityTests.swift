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
        for count in [0, 4, 5, 24, 25, 49, 50, 64, 65, 79, 80, 100] {
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
        XCTAssertEqual(DepthHealthGrade(coverage: 0.6499), .broad)
        XCTAssertEqual(DepthHealthGrade(coverage: 0.65), .strong)
        XCTAssertEqual(DepthHealthGrade(coverage: 0.7999), .strong)
        XCTAssertEqual(DepthHealthGrade(coverage: 0.8), .dense)
        XCTAssertEqual(DepthHealthGrade(coverage: 1), .dense)
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

    func testConfidentSkyExcludedFromSurfaceCoverageButWholeImagePreserved() {
        let map = DepthMap(
            imageId: "sky", width: 4, height: 4,
            depth: [0, 0, 0, 0, 0, 0, 0, 0, 10, 10, 10, 10, 10, 10, 10, 10], confidence: [])
        let sky = SkyProbabilityMap(
            imageId: "sky", width: 2, height: 4,
            sky: [255, 255, 204, 204, 0, 0, 0, 0])
        let summary = DepthQualitySummary(map: map, sky: sky)
        XCTAssertEqual(summary.accepted, 8)
        XCTAssertEqual(summary.coverage, 0.5)
        XCTAssertEqual(summary.excludedSkyPixels, 4)
        XCTAssertEqual(summary.surfaceAccepted, 8)
        XCTAssertEqual(summary.surfaceTotal, 12)
        XCTAssertEqual(summary.surfaceCoverage ?? 0, 2.0 / 3, accuracy: 0.0001)
        XCTAssertEqual(summary.grade, .strong)
        XCTAssertEqual(summary.coveredRegions, 8)
        XCTAssertEqual(summary.regionCount, 12)
        // A sky row adjacent to a non-sky row remains eligible at the skyline.
        XCTAssertEqual(sky.confidentCells(), [true, true, false, false, false, false, false, false])
    }

    func testAllSkyAndUncertainSkyCannotProduceMisleadingDenseGrade() {
        let map = DepthMap(imageId: "sky", width: 2, height: 1, depth: [10, 0], confidence: [0.9, 0])
        let sky = SkyProbabilityMap(imageId: "sky", width: 1, height: 1, sky: [204])
        let excluded = DepthQualitySummary(map: map, sky: sky)
        XCTAssertEqual(excluded.accepted, 1)
        XCTAssertEqual(excluded.surfaceAccepted, 0)
        XCTAssertEqual(excluded.surfaceTotal, 0)
        XCTAssertEqual(excluded.surfaceCoverage, 0)
        XCTAssertEqual(excluded.regionCount, 0)
        XCTAssertEqual(excluded.grade, .empty)
        let uncertain = DepthQualitySummary(
            map: map, sky: SkyProbabilityMap(imageId: "sky", width: 1, height: 1, sky: [203]))
        XCTAssertEqual(uncertain.excludedSkyPixels, 0)
        XCTAssertEqual(uncertain.surfaceCoverage, 0.5)
        XCTAssertEqual(uncertain.grade, .broad)
    }

    func testMaskSamplingMatchesConservativeFourCellRuleAtDifferentResolutions() {
        let sky = SkyProbabilityMap(imageId: "sky", width: 2, height: 2, sky: [255, 203, 255, 255])
        XCTAssertEqual(sky.confidentCells(), [false, false, true, true])
        let map = DepthMap(
            imageId: "sky", width: 4, height: 4, depth: [Float](repeating: 10, count: 16), confidence: [])
        let summary = DepthQualitySummary(map: map, sky: sky)
        XCTAssertEqual(summary.excludedSkyPixels, 4)
        XCTAssertEqual(summary.surfaceAccepted, 12)
        XCTAssertEqual(summary.surfaceCoverage, 1)
    }

    func testMissingMismatchedAndMalformedSkyFallBackToWholeImage() {
        let map = DepthMap(imageId: "depth", width: 2, height: 1, depth: [10, 0], confidence: [])
        let masks: [SkyProbabilityMap?] = [
            nil,
            SkyProbabilityMap(imageId: "different", width: 1, height: 1, sky: [255]),
            SkyProbabilityMap(imageId: "depth", width: 2, height: 1, sky: [255]),
            SkyProbabilityMap(imageId: "depth", width: Int.max, height: 2, sky: []),
            SkyProbabilityMap(imageId: "depth", width: 0, height: 0, sky: []),
        ]
        for sky in masks {
            let summary = DepthQualitySummary(map: map, sky: sky)
            XCTAssertNil(summary.surfaceCoverage)
            XCTAssertFalse(summary.hasSkyMask)
            XCTAssertEqual(summary.grade, .broad)
        }
    }
}
