import SwiftUI

struct ArtifactTreeView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @StateObject private var depthQuality: DepthStageQualityViewModel
    @State private var expandedGroups: Set<PipelineGroup> = [.images]

    @MainActor
    init(workspace: WorkspaceViewModel, depthQuality: DepthStageQualityViewModel? = nil) {
        self.workspace = workspace
        _depthQuality = StateObject(wrappedValue: depthQuality ?? DepthStageQualityViewModel())
    }

    private var photos: [Photo] { workspace.inspection?.project.images ?? [] }

    private var activeGroup: PipelineGroup? {
        guard let progress = workspace.workingRun?.progress, progress.phase != "checking",
            let stage = PipelineStage(rawValue: progress.stage)
        else { return nil }
        return PipelineGroup.allCases.first { $0.stages.contains(stage) }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 3) {
                ForEach(PipelineGroup.allCases) { group in
                    groupNode(group)
                }
            }.padding(8)
        }
        .accessibilityLabel("Workspace artifacts")
        .onChange(of: workspace.destination, initial: true) { _, destination in
            if let group = PipelineGroup.allCases.first(where: { $0.contains(destination) }) {
                expandedGroups.insert(group)
            }
        }
        .onChange(of: activeGroup, initial: true) { _, group in
            if let group { expandedGroups.insert(group) }
        }
        .task(id: workspace.depthQualityRequest) {
            await depthQuality.load(workspace.depthQualityRequest)
        }
    }

    private func groupNode(_ group: PipelineGroup) -> some View {
        let activity = workspace.workingRun?.progress.flatMap { progress in
            group.stages.contains(where: { $0.rawValue == progress.stage }) && progress.phase != "checking"
                ? progress : nil
        }
        let outdated = group.stages.contains { workspace.stageStatus($0) == "Out of date" }
        let saved = group.stages.filter { workspace.displayedRun?.artifacts[$0.rawValue] != nil }.count
        let symbol =
            activity != nil
            ? "arrow.trianglehead.2.clockwise.rotate.90"
            : outdated
                ? "exclamationmark.triangle.fill"
                : saved == group.stages.count
                    ? "checkmark.circle.fill" : saved > 0 ? "circle.lefthalf.filled" : "circle"
        let color: Color =
            activity != nil ? .accentColor : outdated ? .orange : saved == group.stages.count ? .green : .secondary
        return ArtifactTreeGroup(
            group: group, selected: group.contains(workspace.destination),
            status: groupStatus(group, activity: activity, saved: saved, outdated: outdated),
            statusSymbol: symbol, statusColor: color, activity: activity, canRun: workspace.canRunGroups,
            run: { workspace.start(group: group) }, select: { workspace.showArtifact(group.target) },
            expanded: Binding(
                get: { expandedGroups.contains(group) },
                set: { expanded in
                    if expanded { expandedGroups.insert(group) } else { expandedGroups.remove(group) }
                })
        ) {
            if group.includesPhotographs {
                ArtifactTreeNode(
                    title: "Photographs", symbol: "photo.on.rectangle.angled",
                    selected: workspace.destination == .images,
                    select: { workspace.destination = .images }
                ) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(
                            "\(photos.count.formatted()) photographs · \(photos.filter(\.enabled).count.formatted()) enabled"
                        )
                        Text("\(photos.filter { $0.gps != nil }.count.formatted()) with GPS")
                    }
                }
            }
            ForEach(group.stages) { stage in
                stageNode(stage)
            }
        }
        .contextMenu {
            Button("Run \(group.title)") { workspace.start(group: group) }
                .disabled(!workspace.canRunGroups)
            Button("View \(group.target.title)") { workspace.showArtifact(group.target) }
            Button("Show artifact files") { workspace.revealArtifact(group.target) }
                .disabled(workspace.displayedRun?.artifacts[group.target.rawValue] == nil)
        }
        .help(group.isExpandable ? group.runHelp : group.target.explanation)
    }

    private func groupStatus(_ group: PipelineGroup, activity: RunProgress?, saved: Int, outdated: Bool) -> String {
        if let activity {
            let stage = PipelineStage(rawValue: activity.stage)?.shortTitle ?? group.title
            return workspace.cancelRequested ? "Stopping · \(stage)" : "Processing · \(stage)"
        }
        if outdated { return "Out of date" }
        if !group.isExpandable { return workspace.stageStatus(group.target) }
        if saved > 0 { return "\(saved)/\(group.stages.count) stages saved" }
        return group.stages.map(\.shortTitle).joined(separator: " → ")
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
                .disabled(!workspace.canRunGroups)
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
