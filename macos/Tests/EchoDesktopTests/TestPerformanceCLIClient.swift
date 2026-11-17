import Foundation

@testable import EchoDesktop

final class TestPerformanceCLIClient: CLIClient {
    var documents: [PerformanceTraceDocument] = []
    var artifacts: [String: [String: String]] = [:]
    var missingAttempts: Set<String> = []
    private(set) var requests: [[String]] = []

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        let command = Array(arguments.dropFirst(2))
        requests.append(command)
        if command == ["trace", "list", "--all"] {
            return try JSONEncoder().encode(documents.map(\.summary))
        }
        if command.prefix(2) == ["trace", "show"], !missingAttempts.contains(command[2]),
            let document = documents.first(where: { $0.summary.id == command[2] })
        {
            return try JSONEncoder().encode(document)
        }
        if command.prefix(2) == ["run", "status"], let saved = artifacts[command[2]] {
            return try JSONSerialization.data(withJSONObject: [
                "id": command[2], "status": "completed", "target": "features", "artifacts": saved,
                "snapshot": [
                    "id": "project", "name": "Project", "revision": 1, "images": [], "settings": [:], "hints": [],
                ],
            ])
        }
        throw CLIError(message: "Unavailable test data: \(command)")
    }
}
