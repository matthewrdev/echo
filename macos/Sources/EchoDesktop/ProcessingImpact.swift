import Foundation

struct ProcessingImpact {
    func firstChangedStage(from old: ProcessingSettings, to new: ProcessingSettings) -> PipelineStage? {
        let features =
            old.featureWidth != new.featureWidth || old.featureLimit != new.featureLimit
            || old.detectLines != new.detectLines || old.lineLimit != new.lineLimit
            || old.lineMinimumLength != new.lineMinimumLength
        if features { return .features }
        let matching =
            old.pairWindow != new.pairWindow || old.matchAllPairs != new.matchAllPairs
            || old.gpsPairNeighbours != new.gpsPairNeighbours || old.matchRatioThreshold != new.matchRatioThreshold
            || old.matchDistanceThreshold != new.matchDistanceThreshold
        if matching { return .matching }
        if old.bundleIterations != new.bundleIterations { return .alignment }
        let depth =
            old.depthWidth != new.depthWidth || old.depthPlanes != new.depthPlanes
            || old.depthRefinementSamples != new.depthRefinementSamples || old.depthNeighbours != new.depthNeighbours
            || old.depthNear != new.depthNear || old.depthFar != new.depthFar
        if depth { return .depth }
        let cloud =
            old.cloudNeighbours != new.cloudNeighbours || old.cloudMinimumSupport != new.cloudMinimumSupport
            || old.cloudDepthTolerance != new.cloudDepthTolerance
        if cloud { return .cloud }
        if old.fusionDetailMultiplier != new.fusionDetailMultiplier || old.faceLimit != new.faceLimit { return .fusion }
        if old.textureSize != new.textureSize || old.textureJpegQuality != new.textureJpegQuality
            || old.textureChartFaces != new.textureChartFaces || old.textureChartAngle != new.textureChartAngle
            || old.textureGutter != new.textureGutter
        {
            return .texture
        }
        if old.exportValidate != new.exportValidate { return .export }
        return nil
    }

    func firstChangedStage(from old: ProjectData, to new: ProjectData) -> PipelineStage? {
        var changes: [PipelineStage] = []
        for stage in PipelineStage.allCases {
            if old.stageRevisions?[stage.rawValue] != new.stageRevisions?[stage.rawValue] { changes.append(stage) }
        }
        if let settings = firstChangedStage(from: old.settings, to: new.settings) { changes.append(settings) }
        if old.images.filter(\.enabled).map(\.id).sorted() != new.images.filter(\.enabled).map(\.id).sorted() {
            changes.append(.features)
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = .sortedKeys
        for type in ["camera", "tie", "overlap"] {
            let before = old.hints.filter { $0.enabled && $0.type == type }.sorted { $0.id < $1.id }
            let after = new.hints.filter { $0.enabled && $0.type == type }.sorted { $0.id < $1.id }
            if (try? encoder.encode(before)) != (try? encoder.encode(after)) {
                changes.append(type == "camera" ? .alignment : .matching)
            }
        }
        return PipelineStage.allCases.first { changes.contains($0) }
    }

    func stages(from stage: PipelineStage) -> [PipelineStage] {
        Array(PipelineStage.allCases.drop { $0 != stage })
    }

    func warning(from stage: PipelineStage) -> String {
        "Needs rebuilding: " + stages(from: stage).map(\.shortTitle).joined(separator: " → ")
            + ". Saved runs remain available with their original inputs."
    }
}
