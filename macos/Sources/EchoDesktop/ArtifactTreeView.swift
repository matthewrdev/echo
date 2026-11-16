import SwiftUI

struct ArtifactTreeView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @StateObject private var depthQuality: DepthStageQualityViewModel

    @MainActor
    init(workspace: WorkspaceViewModel, depthQuality: DepthStageQualityViewModel? = nil) {
        self.workspace = workspace
        _depthQuality = StateObject(wrappedValue: depthQuality ?? DepthStageQualityViewModel())
    }

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
        .task(id: workspace.depthQualityRequest) {
            await depthQuality.load(workspace.depthQualityRequest)
        }
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
            statusDetail: stage == .depth ? depthQuality.summary?.label : nil,
            statusDetailColor: depthQualityColor,
            statusDetailHelp: stage == .depth ? depthQuality.summary?.help ?? "" : "",
            isProcessing: activity != nil,
            activity: activity, activityHelp: activityHelp,
            select: { workspace.showArtifact(stage) }
        ) {
            Text(summary(stage)).lineLimit(2)
                .help(
                    stage == .depth
                        ? depthQuality.error ?? depthQuality.summary?.help ?? stage.explanation : stage.explanation)
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
        case .depth:
            if let summary = depthQuality.summary { return summary.detail }
            if depthQuality.loading { return "Measuring depth coverage…" }
            if depthQuality.error != nil { return "Depth coverage unavailable" }
            return "Depth & confidence maps"
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

    private var depthQualityColor: Color {
        switch depthQuality.summary?.grade {
        case .empty, .poor: return .red
        case .limited, .partial: return .orange
        case .broad: return .blue
        case .strong, .dense: return .green
        case nil: return .secondary
        }
    }
}
