import SwiftUI

struct ProjectSidebar: View {
    @ObservedObject var workspace: WorkspaceViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 16) {
                Button("Project settings", systemImage: "gearshape") { workspace.sheet = .settings }
                    .help("Project settings")
                Button("Georeference", systemImage: "globe") { workspace.sheet = .georeference }
                    .help("Georeference")
                Button("Placement hints", systemImage: "pin") { workspace.sheet = .hints }
                    .help("Placement hints")
                Button("Generated files", systemImage: "externaldrive") { workspace.sheet = .storage }
                    .help("Generated files")
                Button("Performance", systemImage: "chart.bar.xaxis") { workspace.sheet = .performance }
                    .help("Recorded step timings, timeline and run comparisons")
                Button("Saved runs", systemImage: "clock.arrow.circlepath") { workspace.sheet = .history }
                    .help("Saved runs")
            }
            .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).disabled(workspace.inspection == nil)
            .padding(14)
            Divider()
            Text("WORKSPACE").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                .padding(.horizontal, 14).padding(.top, 12).padding(.bottom, 4)
            ArtifactTreeView(workspace: workspace)
            Divider()
            Button {
                workspace.sheet = .history
            } label: {
                Label(workspace.viewingDescription, systemImage: "clock.arrow.circlepath")
                    .font(.caption).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain).padding(14).help("Choose a saved run")
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(.bar)
    }
}
