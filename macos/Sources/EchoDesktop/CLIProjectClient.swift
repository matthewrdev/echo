import Foundation

final class CLIProjectClient: ProjectClient {
    private let cli: CLIClient

    init(cli: CLIClient) {
        self.cli = cli
    }

    func pollFiles(root: String, selectedRun: String?, activeRun: String?) async -> WorkspacePollFiles? {
        await Task.detached(priority: .utility) {
            WorkspacePollFiles.read(root: root, selectedRun: selectedRun, activeRun: activeRun)
        }.value
    }

    func project(root: String) async throws -> ProjectData {
        let bytes = try await cli.execute(["--project", root, "project", "info"], progress: { _ in })
        return try JSONDecoder().decode(ProjectData.self, from: bytes)
    }

    func storage(root: String) async throws -> ArtifactStorageInventory {
        let bytes = try await cli.execute(["--project", root, "storage", "list"], progress: { _ in })
        return try JSONDecoder().decode(ArtifactStorageInventory.self, from: bytes)
    }

    func deletionPlan(root: String, selection: ArtifactDeletionSelection) async throws -> ArtifactDeletionPlan {
        let arguments = ["--project", root, "storage", "plan"] + selection.arguments
        let bytes = try await cli.execute(arguments, progress: { _ in })
        return try JSONDecoder().decode(ArtifactDeletionPlan.self, from: bytes)
    }

    func inspect(root: String, run: String) async throws -> Inspection {
        try await inspect(root: root, run: run, cached: nil)
    }

    func inspect(root: String, run: String, cached: Inspection?) async throws -> Inspection {
        let summary = ["--project", root, "inspect", "--run", run, "--summary"]
        var arguments = summary
        let token = cached.map { "\($0.project.id):\($0.project.revision)" } ?? "none"
        arguments += ["--known-project", token]
        if let snapshotToken = cached?.run?.snapshotToken {
            arguments += ["--known-snapshot", snapshotToken]
        }
        let bytes: Data
        do {
            bytes = try await cli.executeUnlogged(arguments)
        } catch let error as CLIError where error.message.hasPrefix("Unknown option: --known-") {
            bytes = try await cli.executeUnlogged(summary)
        }
        return try JSONDecoder().decode(WorkspaceInspectionResponse.self, from: bytes).inspection(cached: cached)
    }

    func run(root: String, id: String) async throws -> RunData {
        let bytes = try await cli.executeUnlogged(["--project", root, "run", "status", id])
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

    func performanceTraces(root: String) async throws -> [PerformanceTraceSummary] {
        let bytes = try await cli.execute(["--project", root, "trace", "list", "--all"], progress: { _ in })
        return try JSONDecoder().decode([PerformanceTraceSummary].self, from: bytes)
    }

    func performanceTrace(root: String, run: String, attempt: String) async throws -> PerformanceTraceDocument {
        let bytes = try await cli.execute(
            ["--project", root, "trace", "show", attempt, "--run", run], progress: { _ in })
        return try JSONDecoder().decode(PerformanceTraceDocument.self, from: bytes)
    }

    func runs(root: String) async throws -> [RunSummary] {
        let bytes = try await cli.executeUnlogged(["--project", root, "run", "list"])
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
