import XCTest

@testable import EchoDesktop

final class ProcessingSettingsTests: XCTestCase {
    func testOlderProjectSettingsDecodeAndNewControlsRoundTrip() throws {
        let old = Data(#"{"featureWidth":800,"textureSize":4096}"#.utf8)
        var settings = try JSONDecoder().decode(ProcessingSettings.self, from: old)
        XCTAssertEqual(settings.featureWidth, 800)
        XCTAssertEqual(settings.cloudNeighbours, 8)
        XCTAssertEqual(settings.textureChartFaces, 512)
        settings.depthWidth = 4096
        settings.depthRefinementSamples = 32
        settings.depthNeighbours = 8
        settings.matchAllPairs = true
        settings.cloudMinimumSupport = 3
        settings.fusionDetailMultiplier = 2
        settings.textureJpegQuality = 99
        settings.textureGutter = 8
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(restored.depthWidth, 4096)
        XCTAssertEqual(restored.depthRefinementSamples, 32)
        XCTAssertEqual(restored.depthNeighbours, 8)
        XCTAssertTrue(restored.matchAllPairs)
        XCTAssertEqual(restored.cloudMinimumSupport, 3)
        XCTAssertEqual(restored.fusionDetailMultiplier, 2)
        XCTAssertEqual(restored.textureJpegQuality, 99)
        XCTAssertEqual(restored.textureGutter, 8)
    }

    func testDependencyWarningsStartAtTheEarliestAffectedStage() {
        let original = ProcessingSettings()
        var changed = original
        changed.textureJpegQuality = 99
        let impact = ProcessingImpact()
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .texture)
        changed.cloudNeighbours = 12
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .cloud)
        XCTAssertEqual(impact.stages(from: .cloud), [.cloud, .fusion, .mesh, .texture, .export])
        changed = original
        changed.depthRefinementSamples = 32
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .depth)
        changed = original
        changed.faceLimit = 2_000_000
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .fusion)
    }
}
