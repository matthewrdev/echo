import Foundation

struct ProcessingSettings: Codable {
    var colourGrade: ColourGrade?
    var gpsPolygons: [GpsPolygon] = []
    var exclusionRegions: [ExclusionRegion] = []
    var texturePriority: TexturePriorityPainting?
    var processingMemoryMode: ProcessingMemoryMode = .paged
    var processingMemoryBudgetMb: Int = 0
    var processingDiskBudgetMb: Int = 0
    var optimiseVegetation: Bool = true
    var vegetationMeshToleranceMultiplier: Double = 2.5
    var vegetationTextureScale: Double = 0.65
    var recoverCliffDepth: Bool = false
    var depthSuppressSky: Bool = true
    var scaleInvariantFeatures: Bool = true
    var guidedAlignmentPass: Bool = true
    var detectLines: Bool = true
    var lineLimit: Int = 500
    var lineMinimumLength: Int = 24
    var lineGradientThreshold: Double = 0.015
    var lineSplitRegions: Bool = true
    var featureWidth: Int = 2000
    var bundleIterations: Int = 50
    var featureLimit: Int = 6000
    var pairWindow: Int = 5
    var matchAllPairs: Bool = false
    var excludeVegetationFromMatching: Bool = false
    var gpsPairNeighbours: Int = 12
    var frustumPairNeighbours: Int = 32
    var pairSearchDistance: Double = 500
    var matchRatioThreshold: Double = 0.72
    var matchDistanceThreshold: Double = 0.65
    var depthConcurrency: Int = 0
    var computeBackend: ComputeBackend = .auto
    var depthBackend: String = "auto"
    var depthWorkerThreads: Int = 0
    var depthMemoryBudgetMb: Int = 0
    var depthWidth: Int = 2048
    var depthPlanes: Int = 96
    var depthRefinementSamples: Int = 9
    var depthNeighbours: Int = 3
    var depthRangeAutomatic: Bool = true
    var depthNear: Double = 2
    var depthFar: Double = 100
    var subjectDistanceMode: SubjectDistanceMode = .auto
    var subjectDistanceLimit: Double = 200
    var cloudWorkerThreads: Int = 0
    var cloudBatchRows: Int = 8
    var cloudNeighbours: Int = 16
    var cloudMinimumSupport: Int = 2
    var cloudDepthTolerance: Double = 0.01
    var cloudReprojectionTolerancePixels: Double = 1.5
    var cloudMinimumParallaxDegrees: Double = 0.5
    var fusionDetailMultiplier: Double = 1
    var fusionMemoryBudgetMb: Int = 0
    var fusionVoxelSize: Double = 0
    var fusionAllowCoarserResolution: Bool = false
    var fusionIslandMode: IslandTrimMode = .main
    var fusionIslandMinimumSizePercent: Double = 0.1
    var textureSuppressSky: Bool = true
    var textureSkyMarginPixels: Int = 8
    var textureSize: Int = 8192
    var textureSeamBlendPixels: Int = 4
    var textureColourEnhancement: Double = 0.5
    var textureJpegQuality: Int = 92
    var faceLimit: Int = 2_000_000
    var meshCompleteSurface: Bool = true
    var meshCloseHull: Bool = true
    var meshIslandMode: IslandTrimMode = .main
    var meshIslandMinimumAreaPercent: Double = 0.1
    var textureChartFaces: Int = 512
    var textureChartAngle: Double = 60
    var textureGutter: Int = 4
    var exportValidate: Bool = true

    init() {}

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: ProcessingSettingsKey.self)
        colourGrade = try values.decodeIfPresent(ColourGrade.self, forKey: .colourGrade)
        texturePriority = try values.decodeIfPresent(TexturePriorityPainting.self, forKey: .texturePriority)
        gpsPolygons = try values.decodeIfPresent([GpsPolygon].self, forKey: .gpsPolygons) ?? []
        exclusionRegions = try values.decodeIfPresent([ExclusionRegion].self, forKey: .exclusionRegions) ?? []
        processingMemoryMode =
            try values.decodeIfPresent(ProcessingMemoryMode.self, forKey: .processingMemoryMode) ?? .paged
        processingMemoryBudgetMb = try values.decodeIfPresent(Int.self, forKey: .processingMemoryBudgetMb) ?? 0
        processingDiskBudgetMb = try values.decodeIfPresent(Int.self, forKey: .processingDiskBudgetMb) ?? 0
        optimiseVegetation = try values.decodeIfPresent(Bool.self, forKey: .optimiseVegetation) ?? true
        vegetationMeshToleranceMultiplier =
            try values.decodeIfPresent(Double.self, forKey: .vegetationMeshToleranceMultiplier) ?? 2.5
        vegetationTextureScale = try values.decodeIfPresent(Double.self, forKey: .vegetationTextureScale) ?? 0.65
        recoverCliffDepth = try values.decodeIfPresent(Bool.self, forKey: .recoverCliffDepth) ?? false
        depthSuppressSky = try values.decodeIfPresent(Bool.self, forKey: .depthSuppressSky) ?? true
        scaleInvariantFeatures = try values.decodeIfPresent(Bool.self, forKey: .scaleInvariantFeatures) ?? true
        guidedAlignmentPass = try values.decodeIfPresent(Bool.self, forKey: .guidedAlignmentPass) ?? true
        detectLines = try values.decodeIfPresent(Bool.self, forKey: .detectLines) ?? true
        lineLimit = try values.decodeIfPresent(Int.self, forKey: .lineLimit) ?? 500
        lineMinimumLength = try values.decodeIfPresent(Int.self, forKey: .lineMinimumLength) ?? 24
        lineGradientThreshold = try values.decodeIfPresent(Double.self, forKey: .lineGradientThreshold) ?? 0.015
        lineSplitRegions = try values.decodeIfPresent(Bool.self, forKey: .lineSplitRegions) ?? true
        featureWidth = try values.decodeIfPresent(Int.self, forKey: .featureWidth) ?? 2000
        bundleIterations = try values.decodeIfPresent(Int.self, forKey: .bundleIterations) ?? 50
        featureLimit = try values.decodeIfPresent(Int.self, forKey: .featureLimit) ?? 6000
        pairWindow = try values.decodeIfPresent(Int.self, forKey: .pairWindow) ?? 5
        matchAllPairs = try values.decodeIfPresent(Bool.self, forKey: .matchAllPairs) ?? false
        excludeVegetationFromMatching =
            try values.decodeIfPresent(Bool.self, forKey: .excludeVegetationFromMatching) ?? false
        gpsPairNeighbours = try values.decodeIfPresent(Int.self, forKey: .gpsPairNeighbours) ?? 12
        frustumPairNeighbours = try values.decodeIfPresent(Int.self, forKey: .frustumPairNeighbours) ?? 32
        pairSearchDistance = try values.decodeIfPresent(Double.self, forKey: .pairSearchDistance) ?? 500
        matchRatioThreshold = try values.decodeIfPresent(Double.self, forKey: .matchRatioThreshold) ?? 0.72
        matchDistanceThreshold = try values.decodeIfPresent(Double.self, forKey: .matchDistanceThreshold) ?? 0.65
        depthConcurrency = try values.decodeIfPresent(Int.self, forKey: .depthConcurrency) ?? 0
        computeBackend = try values.decodeIfPresent(ComputeBackend.self, forKey: .computeBackend) ?? .auto
        depthBackend = try values.decodeIfPresent(String.self, forKey: .depthBackend) ?? "auto"
        depthWorkerThreads = try values.decodeIfPresent(Int.self, forKey: .depthWorkerThreads) ?? 0
        depthMemoryBudgetMb = try values.decodeIfPresent(Int.self, forKey: .depthMemoryBudgetMb) ?? 0
        depthWidth = try values.decodeIfPresent(Int.self, forKey: .depthWidth) ?? 2048
        depthPlanes = try values.decodeIfPresent(Int.self, forKey: .depthPlanes) ?? 96
        depthRefinementSamples = try values.decodeIfPresent(Int.self, forKey: .depthRefinementSamples) ?? 9
        depthNeighbours = try values.decodeIfPresent(Int.self, forKey: .depthNeighbours) ?? 3
        depthRangeAutomatic = try values.decodeIfPresent(Bool.self, forKey: .depthRangeAutomatic) ?? true
        depthNear = try values.decodeIfPresent(Double.self, forKey: .depthNear) ?? 2
        depthFar = try values.decodeIfPresent(Double.self, forKey: .depthFar) ?? 100
        subjectDistanceMode =
            try values.decodeIfPresent(SubjectDistanceMode.self, forKey: .subjectDistanceMode) ?? .auto
        subjectDistanceLimit = try values.decodeIfPresent(Double.self, forKey: .subjectDistanceLimit) ?? 200
        cloudWorkerThreads = try values.decodeIfPresent(Int.self, forKey: .cloudWorkerThreads) ?? 0
        cloudBatchRows = try values.decodeIfPresent(Int.self, forKey: .cloudBatchRows) ?? 8
        cloudNeighbours = try values.decodeIfPresent(Int.self, forKey: .cloudNeighbours) ?? 16
        cloudMinimumSupport = try values.decodeIfPresent(Int.self, forKey: .cloudMinimumSupport) ?? 2
        cloudDepthTolerance = try values.decodeIfPresent(Double.self, forKey: .cloudDepthTolerance) ?? 0.01
        cloudReprojectionTolerancePixels =
            try values.decodeIfPresent(Double.self, forKey: .cloudReprojectionTolerancePixels) ?? 1.5
        cloudMinimumParallaxDegrees =
            try values.decodeIfPresent(Double.self, forKey: .cloudMinimumParallaxDegrees) ?? 0.5
        fusionDetailMultiplier = try values.decodeIfPresent(Double.self, forKey: .fusionDetailMultiplier) ?? 1
        fusionMemoryBudgetMb = try values.decodeIfPresent(Int.self, forKey: .fusionMemoryBudgetMb) ?? 0
        fusionVoxelSize = try values.decodeIfPresent(Double.self, forKey: .fusionVoxelSize) ?? 0
        fusionAllowCoarserResolution =
            try values.decodeIfPresent(Bool.self, forKey: .fusionAllowCoarserResolution) ?? false
        fusionIslandMode = try values.decodeIfPresent(IslandTrimMode.self, forKey: .fusionIslandMode) ?? .main
        fusionIslandMinimumSizePercent =
            try values.decodeIfPresent(Double.self, forKey: .fusionIslandMinimumSizePercent) ?? 0.1
        textureSuppressSky = try values.decodeIfPresent(Bool.self, forKey: .textureSuppressSky) ?? true
        textureSkyMarginPixels = try values.decodeIfPresent(Int.self, forKey: .textureSkyMarginPixels) ?? 8
        textureSize = try values.decodeIfPresent(Int.self, forKey: .textureSize) ?? 8192
        textureSeamBlendPixels = try values.decodeIfPresent(Int.self, forKey: .textureSeamBlendPixels) ?? 4
        textureColourEnhancement = try values.decodeIfPresent(Double.self, forKey: .textureColourEnhancement) ?? 0.5
        textureJpegQuality = try values.decodeIfPresent(Int.self, forKey: .textureJpegQuality) ?? 92
        faceLimit = try values.decodeIfPresent(Int.self, forKey: .faceLimit) ?? 2_000_000
        meshCompleteSurface = try values.decodeIfPresent(Bool.self, forKey: .meshCompleteSurface) ?? true
        meshCloseHull = try values.decodeIfPresent(Bool.self, forKey: .meshCloseHull) ?? true
        meshIslandMode = try values.decodeIfPresent(IslandTrimMode.self, forKey: .meshIslandMode) ?? .main
        meshIslandMinimumAreaPercent =
            try values.decodeIfPresent(Double.self, forKey: .meshIslandMinimumAreaPercent) ?? 0.1
        textureChartFaces = try values.decodeIfPresent(Int.self, forKey: .textureChartFaces) ?? 512
        textureChartAngle = try values.decodeIfPresent(Double.self, forKey: .textureChartAngle) ?? 60
        textureGutter = try values.decodeIfPresent(Int.self, forKey: .textureGutter) ?? 4
        exportValidate = try values.decodeIfPresent(Bool.self, forKey: .exportValidate) ?? true
    }

    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: ProcessingSettingsKey.self)
        try values.encodeIfPresent(colourGrade, forKey: .colourGrade)
        if !gpsPolygons.isEmpty { try values.encode(gpsPolygons, forKey: .gpsPolygons) }
        try values.encode(exclusionRegions, forKey: .exclusionRegions)
        try values.encodeIfPresent(texturePriority, forKey: .texturePriority)
        try values.encode(processingMemoryMode, forKey: .processingMemoryMode)
        try values.encode(processingMemoryBudgetMb, forKey: .processingMemoryBudgetMb)
        try values.encode(processingDiskBudgetMb, forKey: .processingDiskBudgetMb)
        try values.encode(optimiseVegetation, forKey: .optimiseVegetation)
        try values.encode(vegetationMeshToleranceMultiplier, forKey: .vegetationMeshToleranceMultiplier)
        try values.encode(vegetationTextureScale, forKey: .vegetationTextureScale)
        try values.encode(recoverCliffDepth, forKey: .recoverCliffDepth)
        try values.encode(depthSuppressSky, forKey: .depthSuppressSky)
        try values.encode(scaleInvariantFeatures, forKey: .scaleInvariantFeatures)
        try values.encode(guidedAlignmentPass, forKey: .guidedAlignmentPass)
        try values.encode(detectLines, forKey: .detectLines)
        try values.encode(lineLimit, forKey: .lineLimit)
        try values.encode(lineMinimumLength, forKey: .lineMinimumLength)
        try values.encode(lineGradientThreshold, forKey: .lineGradientThreshold)
        try values.encode(lineSplitRegions, forKey: .lineSplitRegions)
        try values.encode(featureWidth, forKey: .featureWidth)
        try values.encode(bundleIterations, forKey: .bundleIterations)
        try values.encode(featureLimit, forKey: .featureLimit)
        try values.encode(pairWindow, forKey: .pairWindow)
        try values.encode(matchAllPairs, forKey: .matchAllPairs)
        try values.encode(excludeVegetationFromMatching, forKey: .excludeVegetationFromMatching)
        try values.encode(gpsPairNeighbours, forKey: .gpsPairNeighbours)
        try values.encode(frustumPairNeighbours, forKey: .frustumPairNeighbours)
        try values.encode(pairSearchDistance, forKey: .pairSearchDistance)
        try values.encode(matchRatioThreshold, forKey: .matchRatioThreshold)
        try values.encode(matchDistanceThreshold, forKey: .matchDistanceThreshold)
        try values.encode(depthConcurrency, forKey: .depthConcurrency)
        try values.encode(computeBackend, forKey: .computeBackend)
        try values.encode(depthBackend, forKey: .depthBackend)
        try values.encode(depthWorkerThreads, forKey: .depthWorkerThreads)
        try values.encode(depthMemoryBudgetMb, forKey: .depthMemoryBudgetMb)
        try values.encode(depthWidth, forKey: .depthWidth)
        try values.encode(depthPlanes, forKey: .depthPlanes)
        try values.encode(depthRefinementSamples, forKey: .depthRefinementSamples)
        try values.encode(depthNeighbours, forKey: .depthNeighbours)
        try values.encode(depthRangeAutomatic, forKey: .depthRangeAutomatic)
        try values.encode(depthNear, forKey: .depthNear)
        try values.encode(depthFar, forKey: .depthFar)
        try values.encode(subjectDistanceMode, forKey: .subjectDistanceMode)
        try values.encode(subjectDistanceLimit, forKey: .subjectDistanceLimit)
        try values.encode(cloudWorkerThreads, forKey: .cloudWorkerThreads)
        try values.encode(cloudBatchRows, forKey: .cloudBatchRows)
        try values.encode(cloudNeighbours, forKey: .cloudNeighbours)
        try values.encode(cloudMinimumSupport, forKey: .cloudMinimumSupport)
        try values.encode(cloudDepthTolerance, forKey: .cloudDepthTolerance)
        try values.encode(cloudReprojectionTolerancePixels, forKey: .cloudReprojectionTolerancePixels)
        try values.encode(cloudMinimumParallaxDegrees, forKey: .cloudMinimumParallaxDegrees)
        try values.encode(fusionDetailMultiplier, forKey: .fusionDetailMultiplier)
        try values.encode(fusionMemoryBudgetMb, forKey: .fusionMemoryBudgetMb)
        try values.encode(fusionVoxelSize, forKey: .fusionVoxelSize)
        try values.encode(fusionAllowCoarserResolution, forKey: .fusionAllowCoarserResolution)
        try values.encode(fusionIslandMode, forKey: .fusionIslandMode)
        try values.encode(fusionIslandMinimumSizePercent, forKey: .fusionIslandMinimumSizePercent)
        try values.encode(textureSuppressSky, forKey: .textureSuppressSky)
        try values.encode(textureSkyMarginPixels, forKey: .textureSkyMarginPixels)
        try values.encode(textureSize, forKey: .textureSize)
        try values.encode(textureSeamBlendPixels, forKey: .textureSeamBlendPixels)
        try values.encode(textureColourEnhancement, forKey: .textureColourEnhancement)
        try values.encode(textureJpegQuality, forKey: .textureJpegQuality)
        try values.encode(faceLimit, forKey: .faceLimit)
        try values.encode(meshCompleteSurface, forKey: .meshCompleteSurface)
        try values.encode(meshCloseHull, forKey: .meshCloseHull)
        try values.encode(meshIslandMode, forKey: .meshIslandMode)
        try values.encode(meshIslandMinimumAreaPercent, forKey: .meshIslandMinimumAreaPercent)
        try values.encode(textureChartFaces, forKey: .textureChartFaces)
        try values.encode(textureChartAngle, forKey: .textureChartAngle)
        try values.encode(textureGutter, forKey: .textureGutter)
        try values.encode(exportValidate, forKey: .exportValidate)
    }

    func summary(for stage: PipelineStage) -> String {
        switch stage {
        case .features:
            return
                "\(scaleInvariantFeatures ? "Scale-space" : "Harris") · Up to \(featureLimit) points · \(detectLines ? "up to \(lineLimit) lines + junctions" : "lines off") · \(featureWidth) px"
        case .matching:
            return
                "\(matchAllPairs ? "All image pairs" : "\(frustumPairNeighbours) overlapping views + \(gpsPairNeighbours) GPS neighbours + \(pairWindow) sequence neighbours") · uniqueness \(matchRatioThreshold) · distance \(matchDistanceThreshold)"
        case .alignment:
            return
                "Up to \(bundleIterations) iterations per solve · guided second pass \(guidedAlignmentPass ? "on" : "off")"
        case .depth:
            let subject =
                subjectDistanceMode == .manual
                ? "subject limit \(subjectDistanceLimit) m"
                : subjectDistanceMode == .auto && depthRangeAutomatic ? "adaptive subject limit" : "subject limit off"
            let range = depthRangeAutomatic ? "Automatic scene range" : "\(depthNear)–\(depthFar) m range"
            return
                "\(depthWidth) px · \(depthPlanes) initial samples · \(depthRefinementSamples) refinement samples · \(depthNeighbours)+ adaptive stereo views · \(range) · \(subject)"
        case .cloud:
            return
                "\(cloudNeighbours) neighbours · \(cloudMinimumSupport) required supporting views · \((cloudDepthTolerance * 100).formatted())% relative depth tolerance"
        case .fusion:
            let trimming =
                fusionIslandMode == .off
                ? "island trimming off" : fusionIslandMode == .main ? "main region only" : "trim small regions"
            let resolution =
                fusionVoxelSize > 0
                ? "\((fusionVoxelSize * 100).formatted()) cm voxels"
                : "Automatic resolution · \(fusionDetailMultiplier.formatted())× detail"
            let memory =
                fusionMemoryBudgetMb == 0
                ? "Automatic memory" : "\((Double(fusionMemoryBudgetMb) / 1024).formatted()) GiB budget"
            let fallback =
                fusionVoxelSize == 0
                ? "adaptive voxel budget" : fusionAllowCoarserResolution ? "coarsening allowed" : "preserve resolution"
            return "\(resolution) · \(memory) · \(fallback) · \(trimming)"
        case .mesh:
            let islands =
                meshIslandMode == .main
                ? "main surface only"
                : meshIslandMode == .small
                    ? "trim islands below \(meshIslandMinimumAreaPercent.formatted())% area"
                    : "island trimming off"
            let completion = meshCompleteSurface ? "surface completion on" : "surface completion off"
            return "Up to \(faceLimit.formatted()) triangles · \(completion) · \(islands)"
        case .texture:
            return
                "\(textureSize) × \(textureSize) texture · seam softening \(textureSeamBlendPixels) px · colour finish \(Int((textureColourEnhancement * 100).rounded()))% · JPEG \(textureJpegQuality) · \(textureChartFaces) faces per chart · \(textureChartAngle)° chart angle · \(textureGutter) px gutter · sky guard \(textureSuppressSky ? "\(textureSkyMarginPixels) px" : "off")"
        case .export: return "GLB + georeference sidecar · structural validation \(exportValidate ? "on" : "off")"
        }
    }
}
