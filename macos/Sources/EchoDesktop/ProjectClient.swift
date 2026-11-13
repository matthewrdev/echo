import Foundation

protocol ProjectClient {
    func storage(root: String) async throws -> ArtifactStorageInventory
    func deletionPlan(root: String, artifact: String?) async throws -> ArtifactDeletionPlan
    func inspect(root: String, run: String) async throws -> Inspection
    func run(root: String, id: String) async throws -> RunData
    func metadata(root: String, imageID: String) async throws -> [String: JSONValue]
    func metadataPayload(root: String, imageID: String, tag: String) async throws -> Data
    func runs(root: String) async throws -> [RunSummary]
    func command(root: String?, arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws
    func importValue<T: Encodable>(root: String, command: String, value: T) async throws
}
