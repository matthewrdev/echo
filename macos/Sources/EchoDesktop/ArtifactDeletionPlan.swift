import Foundation

struct ArtifactDeletionPlan: Decodable {
    let token: String
    let purgeAll: Bool
    let bytes: Int64
    let paths: [String]
    let affectedRuns: [String: String]

    var warning: String {
        let space = ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
        if purgeAll {
            return
                "Permanently delete all generated artifacts, exported models, temporary processing files and \(affectedRuns.count) saved runs (\(space)). Photos, previews, project settings and placement hints are kept. The next run starts from scratch. This cannot be undone."
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
