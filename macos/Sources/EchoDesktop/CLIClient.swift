import Foundation

protocol CLIClient {
    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data
}
