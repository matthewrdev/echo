import SwiftUI

struct ArtifactTreeView: View {
    @ObservedObject var workspace: WorkspaceViewModel

    private var photos: [Photo] { workspace.inspection?.project.images ?? [] }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 3) {
                ArtifactTreeNode(
                    title: "Images", symbol: "photo.on.rectangle.angled", selected: workspace.destination == .images,
                    select: { workspace.destination = .images }
                ) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(
                            "\(photos.count.formatted()) photographs · \(photos.filter(\.enabled).count.formatted()) enabled"
                        )
                        Text("\(photos.filter { $0.gps != nil }.count.formatted()) with GPS")
                    }
                }
                ForEach(PipelineStage.allCases) { stage in
                    stageNode(stage)
                }
            }.padding(8)
        }
        .accessibilityLabel("Workspace artifacts")
    }

    private func stageNode(_ stage: PipelineStage) -> some View {
        let activity = workspace.workingRun?.progress.flatMap {
            $0.stage == stage.rawValue && $0.phase != "checking" ? $0 : nil
        }
        let anotherRun = activity != nil && workspace.workingRun?.id != workspace.inspection?.run?.id
        let activityHelp =
            (anotherRun ? "Processing in the active run; the viewer shows a saved run.\n" : "")
            + (workspace.cancelRequested ? workspace.progressMessage + "\n" : "")
            + (activity?.activityDescription ?? "")
        return ArtifactTreeNode(
            title: stage.shortTitle, symbol: stage.symbol, selected: workspace.destination == .artifact(stage),
            statusSymbol: activity == nil ? workspace.stageSymbol(stage) : "arrow.trianglehead.2.clockwise.rotate.90",
            statusColor: activity == nil ? workspace.stageColor(stage) : .accentColor,
            statusHelp: activity == nil ? workspace.stageStatus(stage) : activityHelp,
            activity: activity, activityHelp: activityHelp,
            select: { workspace.showArtifact(stage) }
        ) {
            Text(summary(stage)).lineLimit(2).help(stage.explanation)
        }
        .contextMenu {
            Button("View \(stage.title)") { workspace.showArtifact(stage) }
            Button("Process to \(stage.shortTitle)") { workspace.start(through: stage) }
                .disabled(!workspace.canEdit || photos.filter(\.enabled).count < 2)
            Button("Show artifact files") { workspace.revealArtifact(stage) }
                .disabled(workspace.displayedRun?.artifacts[stage.rawValue] == nil)
        }
    }

    private func summary(_ stage: PipelineStage) -> String {
        switch stage {
        case .features: return "Point & line descriptors"
        case .matching: return "Image pair correspondences"
        case .alignment:
            if let scene = workspace.inspection?.reconstruction {
                return "\(scene.cameras.count.formatted()) cameras · \(scene.unplaced.count.formatted()) unplaced"
            }
            return "Sparse cloud & camera poses"
        case .depth: return "Depth & confidence maps"
        case .cloud:
            if let cloud = workspace.inspection?.cloud {
                return "\(cloud.retainedSamples.formatted()) points"
            }
            return "Supported, coloured 3D points"
        case .fusion: return "Signed-distance volume"
        case .mesh: return "Reconstructed surface"
        case .texture: return "Textured surface & atlas"
        case .export: return "GLB model & georeference"
        }
    }
}
