import SwiftUI

struct GettingStartedView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var renamingProject: RecentProject?

    var body: some View {
        HStack(alignment: .top, spacing: 50) {
            VStack(alignment: .leading, spacing: 18) {
                Image(nsImage: NSApplication.shared.applicationIconImage)
                    .resizable().frame(width: 100, height: 100)
                Text("Echo").font(.system(size: 42, weight: .bold))
                Text("Build a 3D scene from your photographs.")
                    .font(.title3).foregroundStyle(.secondary)
                Text(
                    "Create a project, import a photo folder, then process and inspect each stage. Completed work is saved so you can return later."
                )
                .font(.callout).foregroundStyle(.secondary)
                Button {
                    workspace.createProject()
                } label: {
                    Label("New Project…", systemImage: "folder.badge.plus").frame(maxWidth: .infinity)
                }.buttonStyle(.borderedProminent).controlSize(.large).keyboardShortcut("n")
                Button {
                    workspace.openProject()
                } label: {
                    Label("Open Project…", systemImage: "folder").frame(maxWidth: .infinity)
                }.controlSize(.large).keyboardShortcut("o")
                Text("Open the project folder containing project.json.")
                    .font(.caption).foregroundStyle(.secondary)
                if workspace.openingProject {
                    ProgressView("Opening project…").controlSize(.small)
                }
                if let error = workspace.openingError {
                    Label(error, systemImage: "exclamationmark.triangle")
                        .font(.callout).foregroundStyle(.red).textSelection(.enabled)
                }
            }.frame(width: 310).disabled(workspace.openingProject || workspace.busy)
            VStack(alignment: .leading, spacing: 16) {
                Text("Recent Projects").font(.title2.bold())
                if workspace.recentProjects.isEmpty {
                    ContentUnavailableView(
                        "Your projects will appear here", systemImage: "clock.arrow.circlepath",
                        description: Text("Open or create a project to add it to this list.")
                    )
                    .frame(height: 260)
                } else {
                    ScrollView {
                        LazyVStack(spacing: 8) {
                            ForEach(workspace.recentProjects) { project in
                                Button {
                                    workspace.openRecent(project)
                                } label: {
                                    HStack(alignment: .top, spacing: 12) {
                                        RecentProjectThumbnail(project: project, loader: workspace.projectThumbnails)
                                        VStack(alignment: .leading, spacing: 5) {
                                            Text(project.name).font(.headline)
                                            Text(project.path).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                                            Text(
                                                "Opened \(project.lastOpened.formatted(.relative(presentation: .named)))"
                                            )
                                            .font(.caption2).foregroundStyle(.secondary)
                                        }
                                        Spacer()
                                        Image(systemName: "chevron.right").foregroundStyle(.tertiary)
                                    }.padding(14).frame(maxWidth: .infinity, alignment: .leading)
                                        .background(.quaternary.opacity(0.45), in: RoundedRectangle(cornerRadius: 10))
                                        .contentShape(Rectangle())
                                }.buttonStyle(.plain)
                                    .help("Reopen \(project.path)")
                                    .contextMenu {
                                        Button("Rename…") { renamingProject = project }
                                        Divider()
                                        Button("Remove from recents") { workspace.removeRecent(project) }
                                        Button("Delete from Disk…", role: .destructive) {
                                            workspace.deleteRecent(project)
                                        }
                                    }
                                    .disabled(workspace.openingProject || workspace.busy)
                            }
                        }
                    }.frame(maxHeight: 460)
                }
            }.frame(minWidth: 390, maxWidth: 480, alignment: .leading)
        }
        .padding(50).frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(nsColor: .windowBackgroundColor))
        .sheet(item: $renamingProject) { project in
            RenameProjectView(project: project, workspace: workspace)
        }
    }
}
