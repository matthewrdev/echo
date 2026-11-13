enum ProcessingSettingsKey: String, CodingKey {
    case detectLines, lineLimit, lineMinimumLength
    case depthConcurrency, depthWorkerThreads, depthMemoryBudgetMb
    case cloudWorkerThreads, cloudBatchRows
    case cloudReprojectionTolerancePixels, cloudMinimumParallaxDegrees
    case depthRefinementSamples, depthNeighbours
    case textureChartFaces, textureChartAngle, textureGutter
    case featureWidth, bundleIterations, featureLimit, pairWindow, matchAllPairs, gpsPairNeighbours,
        matchRatioThreshold, matchDistanceThreshold, depthWidth, depthPlanes, depthNear, depthFar, cloudNeighbours,
        cloudMinimumSupport, cloudDepthTolerance, fusionDetailMultiplier, textureSize, textureJpegQuality, faceLimit,
        exportValidate
}
