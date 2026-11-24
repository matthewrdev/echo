import Foundation

struct DepthStageQualityRequest: Equatable {
    let root: URL
    let runID: String
    let folder: URL
    let semanticsIndex: URL?
    let isPreview: Bool
    let previewVersion: ArtifactFileVersion?

    init?(root: String, run: RunData?) {
        guard let run, let path = run.artifacts["depth"] ?? run.artifacts["depthPreview"] else { return nil }
        let root = URL(fileURLWithPath: root)
        self.root = root
        runID = run.id
        folder = root.appendingPathComponent(path)
        semanticsIndex = run.artifacts["semantics"].map {
            root.appendingPathComponent($0).appendingPathComponent("units.json")
        }
        isPreview = run.artifacts["depth"] == nil
        previewVersion = isPreview ? ArtifactFileVersion(file: folder.appendingPathComponent("units.bin")) : nil
    }
}
