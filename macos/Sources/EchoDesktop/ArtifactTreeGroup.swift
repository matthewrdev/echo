import SwiftUI

struct ArtifactTreeGroup<Content: View>: View {
    let group: PipelineGroup
    let selected: Bool
    let status: String
    let statusSymbol: String
    let statusColor: Color
    let activity: RunProgress?
    let canRun: Bool
    let run: () -> Void
    let select: () -> Void
    @Binding var expanded: Bool
    @ViewBuilder var content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 4) {
                Button {
                    if group.isExpandable {
                        expanded.toggle()
                    } else {
                        select()
                    }
                } label: {
                    HStack(spacing: 7) {
                        Image(
                            systemName: group.isExpandable
                                ? (expanded ? "chevron.down" : "chevron.right") : group.symbol
                        )
                        .font(.system(size: group.isExpandable ? 9 : 13, weight: .semibold))
                        .frame(width: 18)
                        .foregroundStyle(selected ? Color.accentColor : .secondary)
                        Text(group.title).font(.subheadline.weight(.semibold))
                        Spacer(minLength: 0)
                        ArtifactStatusIcon(symbol: statusSymbol, color: statusColor, isProcessing: activity != nil)
                            .help(status)
                    }
                    .padding(.vertical, 8).padding(.leading, 7).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(
                    group.isExpandable ? "\(expanded ? "Collapse" : "Expand") \(group.title)" : group.title
                )
                .accessibilityValue(status)
                Button(action: run) {
                    Image(systemName: "play.fill")
                        .font(.system(size: 10, weight: .semibold))
                        .frame(width: 28, height: 28).contentShape(Rectangle())
                }
                .buttonStyle(.borderless).disabled(!canRun)
                .accessibilityLabel("Run \(group.title)")
                .accessibilityIdentifier("pipeline.run.\(group.rawValue)")
                .help(group.runHelp)
            }
            .padding(.trailing, 3)
            .background(selected ? Color.accentColor.opacity(0.09) : .clear, in: RoundedRectangle(cornerRadius: 6))

            Text(status).font(.caption).foregroundStyle(statusColor)
                .lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
                .padding(.leading, 32).padding(.trailing, 8).padding(.bottom, 4)
            if let activity, !expanded || !group.isExpandable {
                ProgressView(value: activity.fractionCompleted)
                    .progressViewStyle(.linear).controlSize(.small)
                    .accessibilityLabel("\(group.title) progress")
                    .padding(.leading, 32).padding(.trailing, 8).padding(.bottom, 5)
            }
            if group.isExpandable && expanded {
                VStack(alignment: .leading, spacing: 2, content: content)
                    .padding(.leading, 12)
                    .overlay(alignment: .leading) {
                        Rectangle().fill(.quaternary).frame(width: 1).padding(.leading, 10)
                    }
            }
        }
        .accessibilityIdentifier("pipeline.group.\(group.rawValue)")
    }
}
