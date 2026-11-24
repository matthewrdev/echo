import Foundation

protocol ProjectClient {
    func pollFiles(root: String, selectedRun: String?, activeRun: String?) async -> WorkspacePollFiles?
    func project(root: String) async throws -> ProjectData
    func storage(root: String) async throws -> ArtifactStorageInventory
    func deletionPlan(root: String, selection: ArtifactDeletionSelection) async throws -> ArtifactDeletionPlan
    func inspect(root: String, run: String) async throws -> Inspection
    func inspect(root: String, run: String, cached: Inspection?) async throws -> Inspection
    func run(root: String, id: String) async throws -> RunData
    func metadata(root: String, imageID: String) async throws -> [String: JSONValue]
    func metadataPayload(root: String, imageID: String, tag: String) async throws -> Data
    func performanceTraces(root: String) async throws -> [PerformanceTraceSummary]
    func performanceTrace(root: String, run: String, attempt: String) async throws -> PerformanceTraceDocument
    func runs(root: String) async throws -> [RunSummary]
    func command(root: String?, arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws
    func importValue<T: Encodable>(root: String, command: String, value: T) async throws
}

extension ProjectClient {
    func inspect(root: String, run: String, cached: Inspection?) async throws -> Inspection {
        try await inspect(root: root, run: run)
    }

    func pollFiles(root: String, selectedRun: String?, activeRun: String?) async -> WorkspacePollFiles? {
        nil
    }
}
