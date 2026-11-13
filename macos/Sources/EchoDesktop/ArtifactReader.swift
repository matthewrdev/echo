import Foundation

protocol ArtifactReader {
    func units(root: URL, run: RunData, stage: PipelineStage) async throws -> [ArtifactUnit]
    func load(root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double)
        async throws -> ArtifactPresentation
}
