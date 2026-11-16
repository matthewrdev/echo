import Foundation

@testable import EchoDesktop

actor TestDeferredArtifactReader: ArtifactReader {
    private var continuation: CheckedContinuation<ArtifactPresentation, Error>?
    var started: Bool { continuation != nil }

    func units(root: URL, run: RunData, stage: PipelineStage) async throws -> [ArtifactUnit] { [] }

    func load(
        root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double,
        cloudDetail: CloudDisplayDetail, cloudMinimumSupport: Double, sparseOptions: SparseDisplayOptions
    ) async throws -> ArtifactPresentation {
        try await withCheckedThrowingContinuation { continuation = $0 }
    }

    func finish() {
        continuation?.resume(returning: ArtifactPresentation(title: "Late result", detail: ""))
        continuation = nil
    }
}
