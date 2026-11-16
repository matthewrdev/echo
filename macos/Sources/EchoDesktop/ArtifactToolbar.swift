import SwiftUI

struct ArtifactToolbar: ToolbarContent {
    @ObservedObject var workspace: WorkspaceViewModel

    var body: some ToolbarContent {
        ToolbarItemGroup(placement: .primaryAction) {
            if [.alignment, .cloud, .mesh, .texture, .export].contains(workspace.selectedStage) {
                Button("View on map", systemImage: "map") { workspace.sheet = .georeference }
                    .disabled(workspace.displayedRun?.artifacts["alignment"] == nil)
                    .help("View geographic placement and export for QGIS")
            }
            Button("Process to \(workspace.selectedStage.shortTitle)", systemImage: "play.fill") {
                workspace.start(through: workspace.selectedStage)
            }
            .disabled(
                !workspace.canEdit || (workspace.inspection?.project.images.filter(\.enabled).count ?? 0) < 2
            )
            .help("Process to \(workspace.selectedStage.shortTitle). Reuse compatible saved work.")
            Button("Stage settings", systemImage: "gearshape") { workspace.sheet = .settings }
                .help("\(workspace.selectedStage.shortTitle) settings")
            Menu {
                Button("Process all stages to GLB") { workspace.start(through: .export) }
                    .disabled(!workspace.canEdit)
                Button("Resume this saved run") { workspace.resume() }
                    .disabled(
                        !workspace.canEdit || workspace.displayedRun == nil
                            || workspace.displayedRun?.status == "completed")
                Button("Reset from this stage…") { workspace.reset(from: workspace.selectedStage) }
                    .disabled(!workspace.canEdit)
                Button("Manage generated files…") { workspace.sheet = .storage }
                Divider()
                Button("Show artifact files") { workspace.revealArtifact(workspace.selectedStage) }
                    .disabled(workspace.displayedRun?.artifacts[workspace.selectedStage.rawValue] == nil)
            } label: {
                Label("More stage actions", systemImage: "ellipsis")
            }
            .menuIndicator(.hidden).accessibilityLabel("More stage actions").help("More stage actions")
        }
    }
}
