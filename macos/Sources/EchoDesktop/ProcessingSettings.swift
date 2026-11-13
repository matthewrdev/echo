import Foundation

struct ProcessingSettings: Codable {
    var detectLines: Bool = true
    var lineLimit: Int = 500
    var lineMinimumLength: Int = 24
    var featureWidth: Int = 1000
    var bundleIterations: Int = 25
    var featureLimit: Int = 800
    var pairWindow: Int = 5
    var matchAllPairs: Bool = false
    var gpsPairNeighbours: Int = 3
    var matchRatioThreshold: Double = 0.72
    var matchDistanceThreshold: Double = 0.65
    var depthConcurrency: Int = 2
    var depthWorkerThreads: Int = 0
    var depthMemoryBudgetMb: Int = 1024
    var depthWidth: Int = 1024
    var depthPlanes: Int = 96
    var depthRefinementSamples: Int = 9
    var depthNeighbours: Int = 3
    var depthNear: Double = 2
    var depthFar: Double = 100
    var cloudWorkerThreads: Int = 0
    var cloudBatchRows: Int = 8
    var cloudNeighbours: Int = 8
    var cloudMinimumSupport: Int = 1
    var cloudDepthTolerance: Double = 0.01
    var fusionDetailMultiplier: Double = 1
    var textureSize: Int = 8192
    var textureJpegQuality: Int = 92
    var faceLimit: Int = 1_500_000
    var textureChartFaces: Int = 512
    var textureChartAngle: Double = 60
    var textureGutter: Int = 4
    var exportValidate: Bool = true

    init() {}

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: ProcessingSettingsKey.self)
        detectLines = try values.decodeIfPresent(Bool.self, forKey: .detectLines) ?? true
        lineLimit = try values.decodeIfPresent(Int.self, forKey: .lineLimit) ?? 500
        lineMinimumLength = try values.decodeIfPresent(Int.self, forKey: .lineMinimumLength) ?? 24
        featureWidth = try values.decodeIfPresent(Int.self, forKey: .featureWidth) ?? 1000
        bundleIterations = try values.decodeIfPresent(Int.self, forKey: .bundleIterations) ?? 25
        featureLimit = try values.decodeIfPresent(Int.self, forKey: .featureLimit) ?? 800
        pairWindow = try values.decodeIfPresent(Int.self, forKey: .pairWindow) ?? 5
        matchAllPairs = try values.decodeIfPresent(Bool.self, forKey: .matchAllPairs) ?? false
        gpsPairNeighbours = try values.decodeIfPresent(Int.self, forKey: .gpsPairNeighbours) ?? 3
        matchRatioThreshold = try values.decodeIfPresent(Double.self, forKey: .matchRatioThreshold) ?? 0.72
        matchDistanceThreshold = try values.decodeIfPresent(Double.self, forKey: .matchDistanceThreshold) ?? 0.65
        depthConcurrency = try values.decodeIfPresent(Int.self, forKey: .depthConcurrency) ?? 2
        depthWorkerThreads = try values.decodeIfPresent(Int.self, forKey: .depthWorkerThreads) ?? 0
        depthMemoryBudgetMb = try values.decodeIfPresent(Int.self, forKey: .depthMemoryBudgetMb) ?? 1024
        depthWidth = try values.decodeIfPresent(Int.self, forKey: .depthWidth) ?? 1024
        depthPlanes = try values.decodeIfPresent(Int.self, forKey: .depthPlanes) ?? 96
        depthRefinementSamples = try values.decodeIfPresent(Int.self, forKey: .depthRefinementSamples) ?? 9
        depthNeighbours = try values.decodeIfPresent(Int.self, forKey: .depthNeighbours) ?? 3
        depthNear = try values.decodeIfPresent(Double.self, forKey: .depthNear) ?? 2
        depthFar = try values.decodeIfPresent(Double.self, forKey: .depthFar) ?? 100
        cloudWorkerThreads = try values.decodeIfPresent(Int.self, forKey: .cloudWorkerThreads) ?? 0
        cloudBatchRows = try values.decodeIfPresent(Int.self, forKey: .cloudBatchRows) ?? 8
        cloudNeighbours = try values.decodeIfPresent(Int.self, forKey: .cloudNeighbours) ?? 8
        cloudMinimumSupport = try values.decodeIfPresent(Int.self, forKey: .cloudMinimumSupport) ?? 1
        cloudDepthTolerance = try values.decodeIfPresent(Double.self, forKey: .cloudDepthTolerance) ?? 0.01
        fusionDetailMultiplier = try values.decodeIfPresent(Double.self, forKey: .fusionDetailMultiplier) ?? 1
        textureSize = try values.decodeIfPresent(Int.self, forKey: .textureSize) ?? 8192
        textureJpegQuality = try values.decodeIfPresent(Int.self, forKey: .textureJpegQuality) ?? 92
        faceLimit = try values.decodeIfPresent(Int.self, forKey: .faceLimit) ?? 1_500_000
        textureChartFaces = try values.decodeIfPresent(Int.self, forKey: .textureChartFaces) ?? 512
        textureChartAngle = try values.decodeIfPresent(Double.self, forKey: .textureChartAngle) ?? 60
        textureGutter = try values.decodeIfPresent(Int.self, forKey: .textureGutter) ?? 4
        exportValidate = try values.decodeIfPresent(Bool.self, forKey: .exportValidate) ?? true
    }

    func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: ProcessingSettingsKey.self)
        try values.encode(detectLines, forKey: .detectLines)
        try values.encode(lineLimit, forKey: .lineLimit)
        try values.encode(lineMinimumLength, forKey: .lineMinimumLength)
        try values.encode(featureWidth, forKey: .featureWidth)
        try values.encode(bundleIterations, forKey: .bundleIterations)
        try values.encode(featureLimit, forKey: .featureLimit)
        try values.encode(pairWindow, forKey: .pairWindow)
        try values.encode(matchAllPairs, forKey: .matchAllPairs)
        try values.encode(gpsPairNeighbours, forKey: .gpsPairNeighbours)
        try values.encode(matchRatioThreshold, forKey: .matchRatioThreshold)
        try values.encode(matchDistanceThreshold, forKey: .matchDistanceThreshold)
        try values.encode(depthConcurrency, forKey: .depthConcurrency)
        try values.encode(depthWorkerThreads, forKey: .depthWorkerThreads)
        try values.encode(depthMemoryBudgetMb, forKey: .depthMemoryBudgetMb)
        try values.encode(depthWidth, forKey: .depthWidth)
        try values.encode(depthPlanes, forKey: .depthPlanes)
        try values.encode(depthRefinementSamples, forKey: .depthRefinementSamples)
        try values.encode(depthNeighbours, forKey: .depthNeighbours)
        try values.encode(depthNear, forKey: .depthNear)
        try values.encode(depthFar, forKey: .depthFar)
        try values.encode(cloudWorkerThreads, forKey: .cloudWorkerThreads)
        try values.encode(cloudBatchRows, forKey: .cloudBatchRows)
        try values.encode(cloudNeighbours, forKey: .cloudNeighbours)
        try values.encode(cloudMinimumSupport, forKey: .cloudMinimumSupport)
        try values.encode(cloudDepthTolerance, forKey: .cloudDepthTolerance)
        try values.encode(fusionDetailMultiplier, forKey: .fusionDetailMultiplier)
        try values.encode(textureSize, forKey: .textureSize)
        try values.encode(textureJpegQuality, forKey: .textureJpegQuality)
        try values.encode(faceLimit, forKey: .faceLimit)
        try values.encode(textureChartFaces, forKey: .textureChartFaces)
        try values.encode(textureChartAngle, forKey: .textureChartAngle)
        try values.encode(textureGutter, forKey: .textureGutter)
        try values.encode(exportValidate, forKey: .exportValidate)
    }

    func summary(for stage: PipelineStage) -> String {
        switch stage {
        case .features:
            return
                "Up to \(featureLimit) points · \(detectLines ? "up to \(lineLimit) lines + junctions" : "lines off") · \(featureWidth) px"
        case .matching:
            return
                "\(matchAllPairs ? "All image pairs" : "Next \(pairWindow) images + \(gpsPairNeighbours) GPS neighbours") · uniqueness \(matchRatioThreshold) · distance \(matchDistanceThreshold)"
        case .alignment: return "Up to \(bundleIterations) bundle-adjustment iterations"
        case .depth:
            return
                "\(depthWidth) px · \(depthPlanes) initial samples · \(depthRefinementSamples) refinement samples · \(depthNeighbours) stereo views · \(depthNear)–\(depthFar) m range"
        case .cloud:
            return
                "\(cloudNeighbours) neighbours · \(cloudMinimumSupport) required supporting views · \((cloudDepthTolerance * 100).formatted())% relative depth tolerance"
        case .fusion:
            return
                "\(fusionDetailMultiplier.formatted())× fusion detail budget · \(faceLimit.formatted()) target triangles"
        case .mesh: return "Up to \(faceLimit.formatted()) triangles"
        case .texture:
            return
                "\(textureSize) × \(textureSize) texture · JPEG \(textureJpegQuality) · \(textureChartFaces) faces per chart · \(textureChartAngle)° chart angle · \(textureGutter) px gutter"
        case .export: return "GLB + georeference sidecar · structural validation \(exportValidate ? "on" : "off")"
        }
    }
}
