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
                    if let operation = workspace.workingRun?.progress?.operation, !operation.isEmpty {
                        Text(operation).font(.subheadline.weight(.semibold))
                    }
                    Text(workspace.progressMessage).font(.subheadline).foregroundStyle(.secondary).textSelection(
                        .enabled)
                    if let resources = workspace.workingRun?.progress?.resources {
                        Text(resources.summary).font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                    }
                    if workspace.cancelRequested, let message = workspace.workingRun?.progress?.message {
                        Text("Last operation: \(message)").font(.caption).foregroundStyle(.secondary)
                    }
                    if workspace.isWorking, let updated = workspace.workingRun?.progress?.updatedAt {
                        TimelineView(.periodic(from: .now, by: 1)) { context in
                            let age = max(0, Int(context.date.timeIntervalSince(updated)))
                            Text(age < 30 ? "Work advanced \(age)s ago" : "No work progress for \(age)s")
                                .font(.caption).foregroundStyle(age < 30 ? Color.secondary : Color.orange)
                        }
                    }
                }
                Spacer(minLength: 10)
                if workspace.activeRun != nil {
                    TimelineView(.periodic(from: .now, by: 1)) { context in
                        let waiting =
                            workspace.cancelRequested
                            && context.date.timeIntervalSince(workspace.cancellationRequestedAt ?? context.date) < 5
                        Button(
                            workspace.cancelRequested ? (waiting ? "Stopping…" : "Retry stop") : "Stop processing",
                            systemImage: "stop.fill"
                        ) {
                            workspace.cancel()
                        }
                        .disabled(waiting)
                        .help(
                            "Stop the current run and retain completed checkpoints. Retry also checks whether the worker has exited."
                        )
                    }
                }
            }
            if workspace.isWorking, let progress = workspace.workingRun?.progress,
                let completed = progress.completed, let total = progress.total, total > 0
            {
                ProgressView(value: Double(completed), total: Double(total))
                HStack {
                    Text(
                        "\(completed) of \(total) \(progress.unit ?? "items") · \(min(100, Int(Double(completed) / Double(total) * 100)))%"
                    )
                    Spacer()
                    Text("\(workspace.savedStageCount) of 9 stages saved")
                }.font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(14).frame(maxWidth: .infinity, alignment: .leading).background(.bar)
    }
}
