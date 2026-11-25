import Foundation

@testable import EchoDesktop

final class TestPerformanceCLIClient: CLIClient, PerformanceTraceReading {
    var documents: [PerformanceTraceDocument] = []
    var artifacts: [String: [String: String]] = [:]
    var missingAttempts: Set<String> = []
    private(set) var requests: [[String]] = []

    func list(root: String) async throws -> [PerformanceTraceSummary] {
        requests.append(["trace", "list", "--all"])
        return documents.map(\.summary)
    }

    func document(root: String, run: String, attempt: String) async throws -> PerformanceTraceDocument {
        requests.append(["trace", "show", attempt, "--run", run])
        guard !missingAttempts.contains(attempt),
            let document = documents.first(where: { $0.summary.id == attempt && $0.summary.runId == run })
        else { throw CLIError(message: "Unavailable test trace: \(attempt)") }
        return document
    }

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        let command = Array(arguments.dropFirst(2))
        requests.append(command)
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
