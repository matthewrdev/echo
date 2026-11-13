import SwiftUI

struct WorkspaceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var artifacts: ArtifactViewModel
    var showRuntimeInfo: () -> Void

    var body: some View {
        Group {
            if workspace.inspection == nil {
                GettingStartedView(workspace: workspace)
            } else {
                projectWorkspace
            }
        }
        .frame(minWidth: 1100, minHeight: 760)
        .task { await workspace.poll() }
        .modifier(ProjectEditConfirmation(workspace: workspace, active: workspace.sheet == nil))
        .sheet(item: $workspace.sheet) {
            WorkspaceModal(sheet: $0, workspace: workspace, artifacts: artifacts)
                .disclosureGroupStyle(ClickableDisclosureStyle())
        }
        .alert(
            "Echo",
            isPresented: Binding(
                get: { workspace.errorMessage != nil },
                set: { if !$0 { workspace.errorMessage = nil } })
        ) {
            Button("OK", role: .cancel) { workspace.errorMessage = nil }
        } message: {
            Text(workspace.errorMessage ?? "")
        }
    }

    private var projectWorkspace: some View {
        HStack(spacing: 0) {
            ProjectSidebar(workspace: workspace)
                .frame(width: 310)
            Divider()
            VStack(spacing: 0) {
                StageTabBar(workspace: workspace)
                Divider()
                GeometryReader { bounds in
                    ArtifactViewer(workspace: workspace, model: artifacts)
                        .frame(width: bounds.size.width, height: bounds.size.height, alignment: .topLeading)
                        .clipped()
                }
                Divider()
                StageLogView(workspace: workspace)
                if workspace.showsProgress {
                    Divider()
                    GlobalProgressView(workspace: workspace)
                }
            }
            .frame(minWidth: 780, maxWidth: .infinity, maxHeight: .infinity)
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                Button("Projects", systemImage: "folder") { workspace.showProjects() }
                    .disabled(!workspace.canEdit)
            }
            ToolbarItem(placement: .principal) {
                Text(workspace.selectedStage.title).font(.headline)
            }
            ToolbarItemGroup {
                Button("Runtime information", systemImage: "info.circle", action: showRuntimeInfo)
                    .help("About Echo and runtime information")
                Button("Open project", systemImage: "folder.badge.plus") { workspace.openProject() }
                    .disabled(!workspace.canEdit)
            }
        }
    }
}
