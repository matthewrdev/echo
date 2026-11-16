import SwiftUI

struct GlobalProgressView: View {
    @ObservedObject var workspace: WorkspaceViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .center, spacing: 12) {
                if workspace.isWorking {
                    ProgressView().controlSize(.small)
                } else {
                    Image(
                        systemName: workspace.inspection?.run?.status == "failed"
                            ? "exclamationmark.triangle" : "checkmark.circle"
                    ).foregroundStyle(.secondary)
                }
                VStack(alignment: .leading, spacing: 4) {
                    Text(workspace.progressActivity).font(.headline).lineLimit(1)
                        .help(workspace.progressActivity)
                    Text(workspace.progressDetail).font(.subheadline.monospacedDigit())
                        .foregroundStyle(.secondary).lineLimit(1).help(workspace.progressMessage)
                        .textSelection(.enabled)
                }
                Spacer(minLength: 10)
                Button("Performance", systemImage: "chart.bar.xaxis") { workspace.sheet = .performance }
                    .help("Recorded step timings, timeline and run comparisons")
                Toggle("Telemetry", systemImage: "chart.xyaxis.line", isOn: $workspace.showsTelemetry)
                    .toggleStyle(.button)
                    .help("Show or hide memory, CPU and thread charts")
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
                        .help("Stop processing and retain completed checkpoints")
                    }
                }
            }
            if workspace.isWorking, let progress = workspace.workingRun?.progress, progress.phase != "checking" {
                if progress.activeStepIndex != nil {
                    StageProgressBar(progress: progress, showsLegend: true)
                } else if let fraction = progress.fractionCompleted {
                    ProgressView(value: fraction)
                }
            }
            if workspace.showsTelemetry {
                Divider()
                TelemetryView(history: workspace.telemetry, isWorking: workspace.isWorking)
            }
        }
        .padding(14).frame(maxWidth: .infinity, alignment: .leading).background(.bar)
    }
}
