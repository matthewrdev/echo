import SwiftUI

struct StageWorkspaceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var artifacts: ArtifactViewModel
    @State private var activityExpanded = true

    var body: some View {
        VSplitView {
            GeometryReader { bounds in
                ArtifactViewer(workspace: workspace, model: artifacts)
                    .frame(width: bounds.size.width, height: bounds.size.height, alignment: .topLeading)
                    .clipped()
            }
            .frame(minHeight: 180)
            StageLogView(workspace: workspace, expanded: $activityExpanded)
                .frame(
                    minHeight: activityExpanded ? 140 : 36,
                    idealHeight: activityExpanded ? 280 : 36,
                    maxHeight: activityExpanded ? .infinity : 36
                )
                .clipped()
        }
    }
}
