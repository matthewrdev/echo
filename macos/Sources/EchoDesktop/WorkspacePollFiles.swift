import Foundation

struct WorkspacePollFiles: Equatable {
    let project: WorkspaceFileStamp
    let imports: WorkspaceFileStamp?
    let previews: [String: WorkspaceFileStamp]
    let runs: [String: WorkspaceFileStamp]
    let progress: [String: WorkspaceFileStamp]
    let events: WorkspaceFileStamp?

    static func read(root path: String, selectedRun: String?, activeRun: String?) -> Self? {
        let root = URL(fileURLWithPath: path, isDirectory: true)
        guard let project = WorkspaceFileStamp(file: root.appendingPathComponent("project.bin")) else { return nil }
        let runsFolder = root.appendingPathComponent("runs", isDirectory: true)
        let entries = (try? FileManager.default.contentsOfDirectory(atPath: runsFolder.path)) ?? []
        var runs: [String: WorkspaceFileStamp] = [:]
        for entry in entries {
            let run = runsFolder.appendingPathComponent(entry, isDirectory: true)
            runs[entry] = WorkspaceFileStamp(file: run.appendingPathComponent("run.json"))
        }
        var progress: [String: WorkspaceFileStamp] = [:]
        for id in [selectedRun, activeRun].compactMap({ $0 }) where runs[id] != nil {
            let run = runsFolder.appendingPathComponent(id, isDirectory: true)
            progress[id] = WorkspaceFileStamp(file: run.appendingPathComponent("progress.bin"))
        }
        var previews: [String: WorkspaceFileStamp] = [:]
        var events: WorkspaceFileStamp?
        if let id = selectedRun, runs[id] != nil {
            let run = runsFolder.appendingPathComponent(id, isDirectory: true)
            events = WorkspaceFileStamp(file: run.appendingPathComponent("events.bin"))
            let previewFiles = [
                "features/units.bin", "matching/pairs.bin", "depth/units.bin", "alignment/preview.bin",
                "cloud/cloud.bin", "fusion/preview.bin",
            ]
            for path in previewFiles {
                previews[path] = WorkspaceFileStamp(file: run.appendingPathComponent("previews/\(path)"))
            }
        }
        let imports = WorkspaceFileStamp(file: root.appendingPathComponent("project.import.bin"))
        return Self(
            project: project, imports: imports, previews: previews, runs: runs, progress: progress, events: events)
    }

    static func readProgress(root: String, run: String) -> RunProgress? {
        let url = URL(fileURLWithPath: root).appendingPathComponent("runs/\(run)/progress.bin")
        guard let bytes = try? Data(contentsOf: url) else { return nil }
        return try? PackedRunProgressReader().read(bytes)
    }
}
