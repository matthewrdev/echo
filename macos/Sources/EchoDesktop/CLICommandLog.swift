import Foundation

actor CLICommandLog {
    private var history: [String: [CLICommandInvocation]] = [:]
    private var errors: [String: String] = [:]
    private var pending: [String: [CLICommandInvocation]] = [:]

    func record(_ invocation: CLICommandInvocation) {
        guard let root = invocation.projectRoot else { return }
        load(root)
        history[root, default: []].append(invocation)
        pending[root, default: []].append(invocation)
        guard
            FileManager.default.fileExists(
                atPath: URL(fileURLWithPath: root).appendingPathComponent("project.json").path)
        else { return }
        do {
            let file = path(root)
            try FileManager.default.createDirectory(
                at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            if !FileManager.default.fileExists(atPath: file.path) {
                FileManager.default.createFile(atPath: file.path, contents: nil)
            }
            let handle = try FileHandle(forWritingTo: file)
            defer { try? handle.close() }
            try handle.seekToEnd()
            var data = Data()
            for entry in pending[root] ?? [] {
                data.append(try JSONEncoder().encode(entry))
                data.append(10)
            }
            try handle.write(contentsOf: data)
            pending[root] = []
            errors[root] = nil
        } catch {
            errors[root] = error.localizedDescription
        }
    }

    func entries(root: String) -> [CLICommandInvocation] {
        let root = URL(fileURLWithPath: root).standardizedFileURL.path
        load(root)
        return history[root] ?? []
    }

    func prune(root: String, before cutoff: Date) throws {
        let root = URL(fileURLWithPath: root).standardizedFileURL.path
        load(root)
        let retained = (history[root] ?? []).filter { $0.timestamp >= cutoff }
        var data = Data()
        let encoder = JSONEncoder()
        for entry in retained {
            data.append(try encoder.encode(entry))
            data.append(10)
        }
        let file = path(root)
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: file, options: .atomic)
        history[root] = retained
        pending[root] = []
        errors[root] = nil
    }

    func persistenceError(root: String) -> String? {
        errors[URL(fileURLWithPath: root).standardizedFileURL.path]
    }

    private func load(_ root: String) {
        guard history[root] == nil else { return }
        guard let data = try? Data(contentsOf: path(root)) else {
            history[root] = []
            return
        }
        history[root] = data.split(separator: 10).compactMap {
            try? JSONDecoder().decode(CLICommandInvocation.self, from: Data($0))
        }
    }

    private func path(_ root: String) -> URL {
        URL(fileURLWithPath: root).appendingPathComponent("logs/desktop-commands.ndjson")
    }
}
