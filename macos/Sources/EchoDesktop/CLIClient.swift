import Foundation

protocol CLIClient {
    func executeUnlogged(_ arguments: [String]) async throws -> Data
    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data
}

extension CLIClient {
    func executeUnlogged(_ arguments: [String]) async throws -> Data {
        try await execute(arguments, progress: { _ in })
    }
}
