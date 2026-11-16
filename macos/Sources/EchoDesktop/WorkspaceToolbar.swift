import SwiftUI

struct WorkspaceToolbar: ToolbarContent {
    @ObservedObject var workspace: WorkspaceViewModel
    var showRuntimeInfo: () -> Void

    var body: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            Button("Back to projects", systemImage: "chevron.left") { workspace.showProjects() }
                .disabled(!workspace.canEdit)
                .help("Back to projects")
        }
        ToolbarItem(placement: .principal) {
            Text(workspace.destination.title).font(.headline)
        }
        switch workspace.destination {
        case .images:
            ImagesToolbar(workspace: workspace)
        case .artifact:
            ArtifactToolbar(workspace: workspace)
        }
        if #available(macOS 26.0, *) {
            ToolbarSpacer(.fixed, placement: .primaryAction)
        } else {
            ToolbarItem(placement: .primaryAction) {
                Color.clear.frame(width: 12).accessibilityHidden(true)
            }
        }
        ToolbarItemGroup(placement: .primaryAction) {
            Button("Runtime information", systemImage: "info.circle", action: showRuntimeInfo)
                .help("About Echo and runtime information")
            Button("Open project", systemImage: "folder.badge.plus") { workspace.openProject() }
                .disabled(!workspace.canEdit)
        }
    }
}
