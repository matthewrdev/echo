import Foundation

@testable import EchoDesktop

actor SlowArtifactPreviewReader: ArtifactReader {
    private(set) var requests: [String] = []
    private(set) var cancellations = 0

    func units(root: URL, run: RunData, stage: PipelineStage) async throws -> [ArtifactUnit] { [] }

    func load(
        root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double,
        cloudDetail: CloudDisplayDetail, cloudMinimumSupport: Double, sparseOptions: SparseDisplayOptions
    ) async throws -> ArtifactPresentation {
        let path = run.artifacts[stage.rawValue + "Preview"] ?? "saved"
        requests.append(path)
        do { try await Task.sleep(for: .milliseconds(150)) } catch {
            cancellations += 1
            throw error
        }
        return ArtifactPresentation(title: "Preview", detail: path, sceneIdentity: run.id)
    }
}
