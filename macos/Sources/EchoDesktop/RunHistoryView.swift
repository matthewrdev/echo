import SwiftUI

struct RunHistoryView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Choose which saved results to inspect. Processing controls always show the active job.")
                .foregroundStyle(.secondary)
            Button("Follow the latest run") { choose("latest") }
            List(workspace.runs) { run in
                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(PipelineStage(rawValue: run.target)?.title ?? run.target).font(.headline)
                        Text("\(run.created) · \(run.id.prefix(8))").font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Text(run.status.capitalized)
                    Button("View results") { choose(run.id) }
                }.padding(.vertical, 5)
            }
        }.padding(20)
    }
    private func choose(_ id: String) {
        workspace.selectedRun = id
        workspace.sheet = nil
        Task { await workspace.reload() }
    }
}
