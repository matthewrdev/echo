import Foundation

final class CLIProjectClient: ProjectClient {
    private let cli: CLIClient

    init(cli: CLIClient) {
        self.cli = cli
    }

    func inspect(root: String, run: String) async throws -> Inspection {
        let bytes = try await cli.execute(["--project", root, "inspect", "--run", run], progress: { _ in })
        return try JSONDecoder().decode(Inspection.self, from: bytes)
    }

    func run(root: String, id: String) async throws -> RunData {
        let bytes = try await cli.execute(["--project", root, "run", "status", id], progress: { _ in })
        return try JSONDecoder().decode(RunData.self, from: bytes)
    }

    func metadata(root: String, imageID: String) async throws -> [String: JSONValue] {
        let bytes = try await cli.execute(["--project", root, "images", "metadata", imageID], progress: { _ in })
        return try JSONDecoder().decode([String: JSONValue].self, from: bytes)
    }

    func runs(root: String) async throws -> [RunSummary] {
        let bytes = try await cli.execute(["--project", root, "run", "list"], progress: { _ in })
        return try JSONDecoder().decode([RunSummary].self, from: bytes)
    }

    func command(root: String?, arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws {
        let prefix = root.map { ["--project", $0] } ?? []
        _ = try await cli.execute(prefix + arguments, progress: progress)
    }

    func importValue<T: Encodable>(root: String, command: String, value: T) async throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("echo-input-\(UUID().uuidString).json")
        try JSONEncoder().encode(value).write(to: file, options: .atomic)
        defer { try? FileManager.default.removeItem(at: file) }
        try await self.command(root: root, arguments: [command, "import", file.path], progress: { _ in })
    }
}
