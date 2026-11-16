import Foundation

struct ArtifactDeletionPlan: Decodable {
    let token: String
    let kind: ArtifactDeletionKind
    let bytes: Int64
    let paths: [String]
    let affectedRuns: [String: String]
    let retainedRuns: [String]
    let removedRuns: [String]
    let logCutoff: String?

    var logCutoffDate: Date? {
        get throws {
            guard let logCutoff else { return nil }
            if let date = try? Date.ISO8601FormatStyle(includingFractionalSeconds: true).parse(logCutoff) {
                return date
            }
            return try Date.ISO8601FormatStyle().parse(logCutoff)
        }
    }

    var warning: String {
        let space = ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
        if kind == .unused {
            let folders =
                paths.prefix(8).joined(separator: "\n")
                + (paths.count > 8 ? "\n… and \(paths.count - 8) more paths" : "")
            let retention =
                retainedRuns.isEmpty
                ? "No saved runs remain. Photos, settings and hints are kept."
                : "Keep the latest run and \(max(0, retainedRuns.count - 1)) supporting runs needed to display its completed stages. Their saved results and resumable checkpoints are kept, along with photos, settings and hints."
            return
                "Delete \(removedRuns.count) older saved runs and their logs, unreferenced artifacts, older performance traces and temporary processing files (\(space)). Older desktop command history is also cleared.\n\n\(retention)\n\nRetained runs: \(retainedRuns.map { String($0.prefix(8)) }.joined(separator: ", "))\nRuns to remove: \(removedRuns.isEmpty ? "None" : removedRuns.map { String($0.prefix(8)) }.joined(separator: ", "))\n\n\(paths.count) files or folders to remove:\n\(folders.isEmpty ? "None" : folders)\n\nThis cannot be undone."
        }
        if kind == .allGenerated {
            return
                "Permanently delete all generated artifacts, exported models, temporary processing files, desktop command history and \(affectedRuns.count) saved runs (\(space)). Photos, previews, project settings and placement hints are kept. The next run starts from scratch. This cannot be undone."
        }
        let runs = affectedRuns.sorted { $0.key < $1.key }.map {
            "\($0.key.prefix(8)): \(PipelineStage(rawValue: $0.value)?.shortTitle ?? $0.value) and later stages"
        }.joined(separator: "\n")
        return
            "Permanently delete \(paths.count) artifact folders (\(space)), including indexes that depend on this artifact.\n\n"
            + (runs.isEmpty
                ? "No saved runs use these artifacts."
                : "Saved results will be invalidated for:\n\(runs)\n\nOther downstream files remain on disk until you delete them or purge all.")
            + "\n\nPhotos, settings and hints are kept. This cannot be undone."
    }
}
