import Foundation

@testable import EchoDesktop

final class TestSavedArtifactCLIClient: CLIClient {
    var status = "running"
    var currentArtifacts: [String: String] = [:]
    private(set) var requestedRuns: [String] = []

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        let command = Array(arguments.dropFirst(2))
        if command.prefix(2) == ["run", "start"] {
            status = "cancelled"
            throw CancellationError()
        }
        if command.first == "inspect" {
            let selected = command[2] == "latest" ? "current" : command[2]
            return try JSONSerialization.data(withJSONObject: [
                "projectRoot": arguments[1], "project": project, "run": run(selected),
            ])
        }
        if command == ["run", "list"] {
            let ids = ["current"] + (1...5).map { "cancelled-\($0)" } + ["saved"]
            return try JSONSerialization.data(
                withJSONObject: ids.map {
                    ["id": $0, "status": $0 == "current" ? status : "cancelled", "target": "fusion", "created": ""]
                })
        }
        if command.prefix(2) == ["run", "status"] {
            requestedRuns.append(command[2])
            return try JSONSerialization.data(withJSONObject: run(command[2]))
        }
        throw CLIError(message: "Unexpected test command: \(command)")
    }

    private var project: [String: Any] {
        ["id": "project", "name": "Project", "revision": 1, "images": [], "settings": [:], "hints": []]
    }

    private func run(_ id: String) -> [String: Any] {
        [
            "id": id, "status": id == "current" ? status : (id == "saved" ? "completed" : "cancelled"),
            "target": "fusion", "snapshot": project, "storageVersion": 3,
            "artifacts": id == "saved"
                ? ["alignment": "artifacts/alignment/saved", "cloud": "artifacts/cloud/saved"] : currentArtifacts,
        ]
    }
}
