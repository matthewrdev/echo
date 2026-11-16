import SwiftUI

struct ArtifactTreeNode<Summary: View>: View {
    let title: String
    let symbol: String
    let selected: Bool
    var statusSymbol: String? = nil
    var statusColor: Color = .secondary
    var statusHelp = ""
    var isProcessing = false
    var activity: RunProgress? = nil
    var activityHelp = ""
    let select: () -> Void
    @ViewBuilder var summary: () -> Summary
    @State private var expanded = true

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 0) {
                Button {
                    expanded.toggle()
                } label: {
                    Image(systemName: expanded ? "chevron.down" : "chevron.right")
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: 24, height: 32).contentShape(Rectangle())
                }
                .accessibilityLabel("\(expanded ? "Collapse" : "Expand") \(title)")
                .help("\(expanded ? "Collapse" : "Expand") artifact summary")
                Button(action: select) {
                    HStack(spacing: 8) {
                        Image(systemName: symbol).foregroundStyle(selected ? Color.accentColor : .secondary)
                            .frame(width: 18)
                        Text(title).font(.subheadline.weight(.semibold)).lineLimit(1)
                        Spacer(minLength: 4)
                        if let statusSymbol {
                            ArtifactStatusIcon(symbol: statusSymbol, color: statusColor, isProcessing: isProcessing)
                                .help(statusHelp)
                        }
                    }
                    .padding(.trailing, 10).frame(height: 32).contentShape(Rectangle())
                }
                .accessibilityAddTraits(selected ? .isSelected : [])
                .accessibilityValue(statusHelp)
            }
            .buttonStyle(.plain)
            .background(selected ? Color.accentColor.opacity(0.13) : .clear, in: RoundedRectangle(cornerRadius: 6))

            if expanded {
                Button(action: select) {
                    summary()
                        .font(.caption).foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .padding(.leading, 42).padding(.trailing, 10).padding(.bottom, 7)
                .overlay(alignment: .leading) {
                    Rectangle().fill(.quaternary).frame(width: 1).padding(.leading, 32).padding(.bottom, 5)
                }
            }

            if let activity {
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text(
                            activity.stageStepSummary ?? activity.stepSummary
                                ?? (activity.phase == "checking" ? "Checking saved work" : "Processing")
                        )
                        .lineLimit(1)
                        Spacer()
                        if let fraction = activity.fractionCompleted {
                            Text(fraction, format: .percent.precision(.fractionLength(0))).monospacedDigit()
                                .help(
                                    activity.activeStepIndex == nil ? "Stage progress" : "Progress of the current step")
                        }
                    }.font(.caption2).foregroundStyle(Color.accentColor)
                    if activity.hasStepPlan {
                        StageProgressBar(progress: activity, showsLegend: false)
                    } else {
                        ProgressView(value: activity.fractionCompleted)
                            .progressViewStyle(.linear).controlSize(.small)
                            .accessibilityLabel("\(title) progress")
                    }
                }
                .help(activityHelp)
                .padding(.leading, 42).padding(.trailing, 10).padding(.bottom, 9)
            }
        }
    }
}
