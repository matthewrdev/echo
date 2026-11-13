enum ProcessingSettingsKey: String, CodingKey {
    case scaleInvariantFeatures, guidedAlignmentPass
    case frustumPairNeighbours, pairSearchDistance
    case detectLines, lineLimit, lineMinimumLength, lineGradientThreshold, lineSplitRegions
    case depthConcurrency, depthWorkerThreads, depthMemoryBudgetMb
    case cloudWorkerThreads, cloudBatchRows
    case cloudReprojectionTolerancePixels, cloudMinimumParallaxDegrees
    case depthRefinementSamples, depthNeighbours, depthRangeAutomatic
    case textureChartFaces, textureChartAngle, textureGutter
    case featureWidth, bundleIterations, featureLimit, pairWindow, matchAllPairs, gpsPairNeighbours,
        matchRatioThreshold, matchDistanceThreshold, depthWidth, depthPlanes, depthNear, depthFar, cloudNeighbours,
        cloudMinimumSupport, cloudDepthTolerance, fusionDetailMultiplier, textureSize, textureJpegQuality, faceLimit,
        exportValidate
}
