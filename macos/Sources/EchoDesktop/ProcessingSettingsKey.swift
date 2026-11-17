enum ProcessingSettingsKey: String, CodingKey {
    case exclusionRegions, texturePriority
    case processingMemoryMode, processingMemoryBudgetMb, processingDiskBudgetMb
    case optimiseVegetation, vegetationMeshToleranceMultiplier, vegetationTextureScale, recoverCliffDepth
    case computeBackend, depthBackend, depthSuppressSky
    case scaleInvariantFeatures, guidedAlignmentPass
    case frustumPairNeighbours, pairSearchDistance
    case detectLines, lineLimit, lineMinimumLength, lineGradientThreshold, lineSplitRegions
    case depthConcurrency, depthWorkerThreads, depthMemoryBudgetMb
    case cloudWorkerThreads, cloudBatchRows
    case fusionMemoryBudgetMb, fusionVoxelSize, fusionAllowCoarserResolution
    case cloudReprojectionTolerancePixels, cloudMinimumParallaxDegrees
    case depthRefinementSamples, depthNeighbours, depthRangeAutomatic
    case textureChartFaces, textureChartAngle, textureGutter
    case meshCompleteSurface, meshIslandMode, meshIslandMinimumAreaPercent
    case fusionIslandMode, fusionIslandMinimumSizePercent, textureSuppressSky, textureSkyMarginPixels
    case featureWidth, bundleIterations, featureLimit, pairWindow, matchAllPairs, gpsPairNeighbours,
        matchRatioThreshold, matchDistanceThreshold, depthWidth, depthPlanes, depthNear, depthFar, cloudNeighbours,
        cloudMinimumSupport, cloudDepthTolerance, fusionDetailMultiplier, textureSize, textureJpegQuality, faceLimit,
        exportValidate
}
