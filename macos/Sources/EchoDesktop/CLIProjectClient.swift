import Foundation

final class CLIProjectClient: ProjectClient {
    private let cli: CLIClient

    init(cli: CLIClient) {
        self.cli = cli
    }

    func project(root: String) async throws -> ProjectData {
        let bytes = try await cli.execute(["--project", root, "project", "info"], progress: { _ in })
        return try JSONDecoder().decode(ProjectData.self, from: bytes)
    }

    func storage(root: String) async throws -> ArtifactStorageInventory {
        let bytes = try await cli.execute(["--project", root, "storage", "list"], progress: { _ in })
        return try JSONDecoder().decode(ArtifactStorageInventory.self, from: bytes)
    }

    func deletionPlan(root: String, artifact: String?) async throws -> ArtifactDeletionPlan {
        let arguments = ["--project", root, "storage", "plan"] + (artifact.map { ["--artifact", $0] } ?? [])
        let bytes = try await cli.execute(arguments, progress: { _ in })
        return try JSONDecoder().decode(ArtifactDeletionPlan.self, from: bytes)
    }

    func inspect(root: String, run: String) async throws -> Inspection {
        let bytes = try await cli.execute(
            ["--project", root, "inspect", "--run", run, "--summary"], progress: { _ in })
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

    func metadataPayload(root: String, imageID: String, tag: String) async throws -> Data {
        let bytes = try await cli.execute(["--project", root, "images", "payload", imageID, tag], progress: { _ in })
        return try JSONDecoder().decode(Data.self, from: bytes)
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
