import SwiftUI

struct GlobalProgressView: View {
    @ObservedObject var workspace: WorkspaceViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack(alignment: .top, spacing: 12) {
                if workspace.isWorking {
                    ProgressView().controlSize(.small).padding(.top, 3)
                } else {
                    Image(
                        systemName: workspace.inspection?.run?.status == "failed"
                            ? "exclamationmark.triangle" : "checkmark.circle"
                    ).foregroundStyle(.secondary)
                }
                VStack(alignment: .leading, spacing: 4) {
                    Text(workspace.progressTitle).font(.headline)
                    Text(workspace.progressMessage).font(.subheadline).foregroundStyle(.secondary).textSelection(
                        .enabled)
                }
                Spacer(minLength: 10)
                if workspace.activeRun != nil {
                    Button(workspace.cancelRequested ? "Stopping…" : "Stop processing", systemImage: "stop.fill") {
                        workspace.cancel()
                    }
                    .disabled(workspace.cancelRequested)
                }
            }
            if workspace.isWorking, let progress = workspace.workingRun?.progress,
                let completed = progress.completed, let total = progress.total, total > 0
            {
                ProgressView(value: Double(completed), total: Double(total))
                HStack {
                    Text("\(min(100, Int(Double(completed) / Double(total) * 100)))% of this step")
                    Spacer()
                    Text("\(workspace.savedStageCount) of 9 stages saved")
                }.font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(14).frame(maxWidth: .infinity, alignment: .leading).background(.bar)
    }
}
