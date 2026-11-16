import Foundation

protocol ArtifactReader {
    func units(root: URL, run: RunData, stage: PipelineStage) async throws -> [ArtifactUnit]
    func load(
        root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double,
        cloudDetail: CloudDisplayDetail, cloudMinimumSupport: Double, sparseOptions: SparseDisplayOptions
    )
        async throws -> ArtifactPresentation
}

extension ArtifactReader {
    func load(
        root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double,
        cloudDetail: CloudDisplayDetail = .automatic, cloudMinimumSupport: Double = 0
    )
        async throws -> ArtifactPresentation
    {
        try await load(
            root: root, run: run, stage: stage, unit: unit, mode: mode, slice: slice,
            cloudDetail: cloudDetail, cloudMinimumSupport: cloudMinimumSupport, sparseOptions: SparseDisplayOptions())
    }
}
