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
        .disabled(workspace.sheet == .georeference)
        .accessibilityHidden(workspace.sheet == .georeference)
        .frame(minWidth: 1100, minHeight: 760)
        .task { await workspace.poll() }
        .modifier(ProjectEditConfirmation(workspace: workspace, active: workspace.sheet == nil))
        .sheet(item: standardSheet) {
            WorkspaceModal(sheet: $0, workspace: workspace, artifacts: artifacts)
                .disclosureGroupStyle(ClickableDisclosureStyle())
        }
        .overlay {
            if workspace.sheet == .georeference {
                GeoreferenceModal(workspace: workspace)
                    .disclosureGroupStyle(ClickableDisclosureStyle())
            }
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

    private var standardSheet: Binding<WorkspaceSheet?> {
        Binding(
            get: { workspace.sheet == .georeference ? nil : workspace.sheet },
            set: { value in
                if workspace.sheet != .georeference { workspace.sheet = value }
            })
    }

    private var projectWorkspace: some View {
        HSplitView {
            ProjectSidebar(workspace: workspace)
                .frame(minWidth: 250, idealWidth: 310, maxWidth: 500, maxHeight: .infinity)
            VStack(spacing: 0) {
                GeometryReader { bounds in
                    Group {
                        switch workspace.destination {
                        case .images:
                            ImagesView(workspace: workspace)
                        case .artifact:
                            ArtifactViewer(workspace: workspace, model: artifacts)
                        }
                    }
                    .frame(width: bounds.size.width, height: bounds.size.height, alignment: .topLeading)
                    .clipped()
                }
                if workspace.destination != .images {
                    Divider()
                    StageLogView(workspace: workspace)
                }
                if workspace.showsProgress {
                    Divider()
                    GlobalProgressView(workspace: workspace)
                }
            }
            .frame(minWidth: 740, maxWidth: .infinity, maxHeight: .infinity)
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                Button("Projects", systemImage: "folder") { workspace.showProjects() }
                    .disabled(!workspace.canEdit)
            }
            ToolbarItem(placement: .principal) {
                Text(workspace.destination.title).font(.headline)
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
