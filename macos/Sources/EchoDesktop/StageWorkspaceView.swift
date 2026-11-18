import SwiftUI

struct StageWorkspaceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var artifacts: ArtifactViewModel
    @AppStorage("workspace.activityExpanded") private var activityExpanded = true
    @AppStorage("workspace.activityHeight") private var activityHeight = 160.0

    var body: some View {
        PersistentSplitView(
            layout: .activity, paneLength: $activityHeight, collapsedLength: activityExpanded ? nil : 36
        ) {
            GeometryReader { bounds in
                ArtifactViewer(workspace: workspace, model: artifacts)
                    .frame(width: bounds.size.width, height: bounds.size.height, alignment: .topLeading)
                    .clipped()
            }
        } second: {
            StageLogView(workspace: workspace, expanded: $activityExpanded)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .clipped()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
