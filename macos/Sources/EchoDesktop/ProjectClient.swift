import Foundation

protocol ProjectClient {
    func inspect(root: String, run: String) async throws -> Inspection
    func run(root: String, id: String) async throws -> RunData
    func metadata(root: String, imageID: String) async throws -> [String: JSONValue]
    func runs(root: String) async throws -> [RunSummary]
    func command(root: String?, arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws
    func importValue<T: Encodable>(root: String, command: String, value: T) async throws
}
