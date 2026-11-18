import SwiftUI

struct WorkspaceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var artifacts: ArtifactViewModel
    var showRuntimeInfo: () -> Void
    @AppStorage("workspace.sidebarWidth") private var sidebarWidth = 280.0

    var body: some View {
        Group {
            if workspace.inspection == nil {
                GettingStartedView(workspace: workspace)
            } else {
                projectWorkspace
            }
        }
        .navigationTitle(workspace.inspection?.project.name ?? "Projects")
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
        PersistentSplitView(layout: .sidebar, paneLength: $sidebarWidth) {
            ProjectSidebar(workspace: workspace)
        } second: {
            VStack(spacing: 0) {
                if let warnings = workspace.inspection?.warnings, !warnings.isEmpty {
                    VStack(alignment: .leading, spacing: 6) {
                        ForEach(warnings, id: \.self) { warning in
                            Label(warning, systemImage: "exclamationmark.triangle")
                        }
                    }
                    .font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading).padding(12)
                    .background(Color.orange.opacity(0.1))
                    Divider()
                }
                if workspace.destination == .images {
                    GeometryReader { bounds in
                        ImagesView(workspace: workspace)
                            .frame(width: bounds.size.width, height: bounds.size.height, alignment: .topLeading)
                            .clipped()
                    }
                } else {
                    StageWorkspaceView(workspace: workspace, artifacts: artifacts)
                }
                if workspace.showsProgress {
                    Divider()
                    GlobalProgressView(workspace: workspace)
                }
            }
            .frame(minWidth: 740, maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .toolbar {
            WorkspaceToolbar(workspace: workspace, showRuntimeInfo: showRuntimeInfo)
        }
    }
}
