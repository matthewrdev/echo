import XCTest

@testable import EchoDesktop

final class ProcessingSettingsTests: XCTestCase {
    func testComputeBackendUsesTypedWireNamesAndInvalidatesFeatures() throws {
        let original = try JSONDecoder().decode(ProcessingSettings.self, from: Data("{}".utf8))
        XCTAssertEqual(original.computeBackend, .auto)
        for backend in ComputeBackend.allCases {
            var settings = original
            settings.computeBackend = backend
            let data = try JSONEncoder().encode(settings)
            let restored = try JSONDecoder().decode(ProcessingSettings.self, from: data)
            XCTAssertEqual(restored.computeBackend, backend)
            let json = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
            XCTAssertEqual(json["computeBackend"] as? String, backend.rawValue)
            if backend != .auto {
                XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: settings), .features)
            }
        }
        for value in ["0", "42", "\"unknown\""] {
            let data = Data(("{\"computeBackend\":" + value + "}").utf8)
            XCTAssertThrowsError(try JSONDecoder().decode(ProcessingSettings.self, from: data))
        }
    }

    func testSkyDepthSettingDefaultsOnAndInvalidatesDepth() throws {
        let original = try JSONDecoder().decode(ProcessingSettings.self, from: Data("{}".utf8))
        XCTAssertTrue(original.depthSuppressSky)
        var changed = original
        changed.depthSuppressSky = false
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .depth)
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(changed))
        XCTAssertFalse(restored.depthSuppressSky)
    }

    func testFusionMemoryAndResolutionRoundTripAndInvalidateFusion() throws {
        let original = try JSONDecoder().decode(ProcessingSettings.self, from: Data("{}".utf8))
        XCTAssertEqual(original.fusionMemoryBudgetMb, 0)
        XCTAssertEqual(original.fusionVoxelSize, 0)
        XCTAssertFalse(original.fusionAllowCoarserResolution)
        var changed = original
        changed.fusionMemoryBudgetMb = 32_768
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .fusion)
        changed = original
        changed.fusionVoxelSize = 0.05
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .fusion)
        changed = original
        changed.fusionAllowCoarserResolution = true
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .fusion)
        changed.fusionMemoryBudgetMb = 32_768
        changed.fusionVoxelSize = 0.05
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(changed))
        XCTAssertEqual(restored.fusionMemoryBudgetMb, 32_768)
        XCTAssertEqual(restored.fusionVoxelSize, 0.05)
        XCTAssertTrue(restored.fusionAllowCoarserResolution)
        XCTAssertTrue(restored.summary(for: .fusion).contains("32 GiB"))
        XCTAssertTrue(restored.summary(for: .fusion).contains("5 cm"))
    }

    func testSemanticSettingsInvalidateTheirConsumers() {
        let original = ProcessingSettings()
        let impact = ProcessingImpact()
        var changed = original
        changed.recoverCliffDepth = true
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .depth)
        changed = original
        changed.optimiseVegetation = true
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .mesh)
        changed = original
        changed.vegetationMeshToleranceMultiplier = 3
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .mesh)
        changed = original
        changed.vegetationTextureScale = 0.5
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .texture)
    }

    func testOlderProjectSettingsDecodeAndNewControlsRoundTrip() throws {
        let old = Data(#"{"featureWidth":800,"textureSize":4096}"#.utf8)
        var settings = try JSONDecoder().decode(ProcessingSettings.self, from: old)
        XCTAssertEqual(settings.featureWidth, 800)
        XCTAssertEqual(settings.cloudNeighbours, 8)
        XCTAssertEqual(settings.textureChartFaces, 512)
        XCTAssertEqual(settings.frustumPairNeighbours, 12)
        XCTAssertTrue(settings.scaleInvariantFeatures)
        XCTAssertTrue(settings.guidedAlignmentPass)
        XCTAssertTrue(settings.depthRangeAutomatic)
        XCTAssertEqual(settings.depthMemoryBudgetMb, 0)
        XCTAssertEqual(settings.depthBackend, "auto")
        XCTAssertEqual(settings.meshIslandMode, .off)
        XCTAssertEqual(settings.meshIslandMinimumAreaPercent, 0.1)
        XCTAssertFalse(settings.optimiseVegetation)
        XCTAssertFalse(settings.recoverCliffDepth)
        settings.optimiseVegetation = true
        settings.recoverCliffDepth = true
        settings.vegetationMeshToleranceMultiplier = 3
        settings.vegetationTextureScale = 0.5
        settings.depthBackend = "metal-validated"
        settings.depthRangeAutomatic = false
        settings.depthNear = 5
        settings.depthFar = 6000
        settings.scaleInvariantFeatures = false
        settings.guidedAlignmentPass = false
        settings.frustumPairNeighbours = 24
        settings.pairSearchDistance = 350
        settings.detectLines = false
        settings.lineLimit = 1200
        settings.lineMinimumLength = 40
        settings.lineGradientThreshold = 0.04
        settings.lineSplitRegions = false
        settings.depthConcurrency = 4
        settings.depthWorkerThreads = 12
        settings.cloudWorkerThreads = 4
        settings.cloudBatchRows = 16
        settings.cloudReprojectionTolerancePixels = 0.8
        settings.cloudMinimumParallaxDegrees = 1.2
        settings.depthMemoryBudgetMb = 2048
        settings.depthWidth = 4096
        settings.depthRefinementSamples = 32
        settings.depthNeighbours = 8
        settings.matchAllPairs = true
        settings.cloudMinimumSupport = 3
        settings.fusionDetailMultiplier = 2
        settings.textureJpegQuality = 99
        settings.textureGutter = 8
        settings.meshIslandMode = .main
        settings.meshIslandMinimumAreaPercent = 2.5
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertTrue(restored.optimiseVegetation)
        XCTAssertTrue(restored.recoverCliffDepth)
        XCTAssertEqual(restored.vegetationMeshToleranceMultiplier, 3)
        XCTAssertEqual(restored.vegetationTextureScale, 0.5)
        XCTAssertFalse(restored.detectLines)
        XCTAssertFalse(restored.scaleInvariantFeatures)
        XCTAssertFalse(restored.guidedAlignmentPass)
        XCTAssertEqual(restored.lineLimit, 1200)
        XCTAssertEqual(restored.lineMinimumLength, 40)
        XCTAssertEqual(restored.lineGradientThreshold, 0.04)
        XCTAssertFalse(restored.lineSplitRegions)
        XCTAssertEqual(restored.depthConcurrency, 4)
        XCTAssertEqual(restored.depthBackend, "metal-validated")
        XCTAssertEqual(restored.depthWorkerThreads, 12)
        XCTAssertEqual(restored.cloudWorkerThreads, 4)
        XCTAssertEqual(restored.cloudBatchRows, 16)
        XCTAssertEqual(restored.cloudReprojectionTolerancePixels, 0.8)
        XCTAssertEqual(restored.cloudMinimumParallaxDegrees, 1.2)
        XCTAssertEqual(restored.depthMemoryBudgetMb, 2048)
        XCTAssertEqual(restored.depthWidth, 4096)
        XCTAssertEqual(restored.depthRefinementSamples, 32)
        XCTAssertEqual(restored.depthNeighbours, 8)
        XCTAssertFalse(restored.depthRangeAutomatic)
        XCTAssertEqual(restored.depthNear, 5)
        XCTAssertEqual(restored.depthFar, 6000)
        XCTAssertTrue(restored.matchAllPairs)
        XCTAssertEqual(restored.cloudMinimumSupport, 3)
        XCTAssertEqual(restored.fusionDetailMultiplier, 2)
        XCTAssertEqual(restored.textureJpegQuality, 99)
        XCTAssertEqual(restored.textureGutter, 8)
        XCTAssertEqual(restored.meshIslandMode, .main)
        XCTAssertEqual(restored.meshIslandMinimumAreaPercent, 2.5)
        XCTAssertEqual(restored.frustumPairNeighbours, 24)
        XCTAssertEqual(restored.pairSearchDistance, 350)
    }

    func testIslandTrimmingModesRoundTripAndInvalidateMeshOnwards() throws {
        let original = ProcessingSettings()
        for mode in [IslandTrimMode.small, .main] {
            var settings = original
            settings.meshIslandMode = mode
            let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(settings))
            XCTAssertEqual(restored.meshIslandMode, mode)
            XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: restored), .mesh)
            XCTAssertTrue(restored.summary(for: .mesh).contains(mode == .main ? "main surface only" : "0.1%"))
        }
        var changed = original
        changed.meshIslandMinimumAreaPercent = 2
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .mesh)
        XCTAssertEqual(ProcessingImpact().stages(from: .mesh), [.mesh, .texture, .export])
    }

    func testIslandModesPreserveWireNamesAndRejectInvalidValues() throws {
        for mode in IslandTrimMode.allCases {
            let source = Data(
                "{\"fusionIslandMode\":\"\(mode.rawValue)\",\"meshIslandMode\":\"\(mode.rawValue)\"}".utf8)
            let settings = try JSONDecoder().decode(ProcessingSettings.self, from: source)
            XCTAssertEqual(settings.fusionIslandMode, mode)
            XCTAssertEqual(settings.meshIslandMode, mode)
            let encoded = try XCTUnwrap(
                JSONSerialization.jsonObject(with: JSONEncoder().encode(settings)) as? [String: Any])
            XCTAssertEqual(encoded["fusionIslandMode"] as? String, mode.rawValue)
            XCTAssertEqual(encoded["meshIslandMode"] as? String, mode.rawValue)
        }
        for field in ["fusionIslandMode", "meshIslandMode"] {
            for value in ["\"invalid\"", "0", "\"0\""] {
                XCTAssertThrowsError(
                    try JSONDecoder().decode(ProcessingSettings.self, from: Data("{\"\(field)\":\(value)}".utf8)))
            }
        }
    }

    func testEarlyTrimmingAndSkyGuardRoundTripAtTheirOwnStages() throws {
        let original = try JSONDecoder().decode(ProcessingSettings.self, from: Data("{}".utf8))
        XCTAssertEqual(original.fusionIslandMode, .off)
        XCTAssertFalse(original.textureSuppressSky)
        XCTAssertEqual(original.textureSkyMarginPixels, 8)
        var changed = original
        changed.fusionIslandMode = .main
        changed.fusionIslandMinimumSizePercent = 2
        changed.textureSuppressSky = true
        changed.textureSkyMarginPixels = 16
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(changed))
        XCTAssertEqual(restored.fusionIslandMode, .main)
        XCTAssertEqual(restored.fusionIslandMinimumSizePercent, 2)
        XCTAssertTrue(restored.textureSuppressSky)
        XCTAssertEqual(restored.textureSkyMarginPixels, 16)
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: restored), .fusion)
        changed = original
        changed.fusionIslandMinimumSizePercent = 2
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .fusion)
        changed = original
        changed.textureSuppressSky = true
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .texture)
        changed = original
        changed.textureSkyMarginPixels = 16
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .texture)
    }

    func testAutomaticDepthMemoryBudgetRoundTripsWithoutInvalidatingSavedMaps() throws {
        let saved = Data(#"{"depthMemoryBudgetMb":1024}"#.utf8)
        let manual = try JSONDecoder().decode(ProcessingSettings.self, from: saved)
        XCTAssertEqual(manual.depthMemoryBudgetMb, 1024)
        var automatic = manual
        automatic.depthMemoryBudgetMb = 0
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(automatic))
        XCTAssertEqual(restored.depthMemoryBudgetMb, 0)
        XCTAssertNil(ProcessingImpact().firstChangedStage(from: manual, to: restored))
    }

    func testDepthBackendChangesInvalidateOnlyDifferentNumericalContracts() throws {
        var automatic = ProcessingSettings()
        XCTAssertEqual(automatic.depthBackend, "auto")
        var metal = automatic
        metal.depthBackend = "metal"
        XCTAssertNil(ProcessingImpact().firstChangedStage(from: automatic, to: metal))
        var cpu = automatic
        cpu.depthBackend = "cpu"
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: automatic, to: cpu), .depth)
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: cpu, to: automatic), .depth)
        var validated = cpu
        validated.depthBackend = "metal-validated"
        XCTAssertNil(ProcessingImpact().firstChangedStage(from: cpu, to: validated))
        let saved = Data(#"{"depthBackend":"cpu"}"#.utf8)
        automatic = try JSONDecoder().decode(ProcessingSettings.self, from: saved)
        XCTAssertEqual(automatic.depthBackend, "cpu")
    }

    func testDependencyWarningsStartAtTheEarliestAffectedStage() {
        let original = ProcessingSettings()
        var changed = original
        changed.textureJpegQuality = 99
        let impact = ProcessingImpact()
        var algorithmChanged = original
        algorithmChanged.scaleInvariantFeatures = false
        XCTAssertEqual(impact.firstChangedStage(from: original, to: algorithmChanged), .features)
        algorithmChanged = original
        algorithmChanged.guidedAlignmentPass = false
        XCTAssertEqual(impact.firstChangedStage(from: original, to: algorithmChanged), .alignment)
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .texture)
        changed.cloudNeighbours = 12
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .cloud)
        XCTAssertEqual(impact.stages(from: .cloud), [.cloud, .fusion, .mesh, .texture, .export])
        changed = original
        changed.cloudReprojectionTolerancePixels = 0.8
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .cloud)
        changed = original
        changed.cloudMinimumParallaxDegrees = 1.2
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .cloud)
        changed = original
        changed.depthRefinementSamples = 32
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .depth)
        changed = original
        changed.depthRangeAutomatic = false
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .depth)
        changed = original
        changed.depthConcurrency = 4
        changed.depthWorkerThreads = 12
        changed.cloudWorkerThreads = 4
        changed.cloudBatchRows = 16
        changed.depthMemoryBudgetMb = 2048
        XCTAssertNil(impact.firstChangedStage(from: original, to: changed))
        changed.detectLines = false
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .features)
        changed = original
        changed.lineGradientThreshold = 0.04
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .features)
        changed = original
        changed.lineSplitRegions = false
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .features)
        changed = original
        changed.faceLimit = 2_000_000
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .mesh)
        changed = original
        changed.frustumPairNeighbours = 24
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .matching)
        changed = original
        changed.pairSearchDistance = 350
        XCTAssertEqual(impact.firstChangedStage(from: original, to: changed), .matching)
    }
}
