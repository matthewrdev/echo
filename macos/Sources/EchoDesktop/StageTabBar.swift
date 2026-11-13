import SwiftUI

struct StageTabBar: View {
    @ObservedObject var workspace: WorkspaceViewModel

    var body: some View {
        HStack(spacing: 2) {
            ForEach(Array(PipelineStage.allCases.enumerated()), id: \.element.id) { index, stage in
                Button {
                    workspace.selectedStage = stage
                } label: {
                    VStack(spacing: 5) {
                        HStack(spacing: 5) {
                            Text("\(index + 1)").monospacedDigit()
                            Image(systemName: workspace.stageSymbol(stage))
                                .foregroundStyle(workspace.stageColor(stage))
                        }.font(.caption2)
                        Text(stage.shortTitle).font(.caption.weight(.semibold)).lineLimit(1)
                            .minimumScaleFactor(0.75)
                        Rectangle().fill(workspace.selectedStage == stage ? Color.accentColor : Color.clear)
                            .frame(height: 3)
                    }
                    .padding(.top, 10).padding(.horizontal, 3)
                    .frame(maxWidth: .infinity)
                    .background(workspace.selectedStage == stage ? Color.accentColor.opacity(0.12) : Color.clear)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("\(stage.title), \(workspace.stageStatus(stage))")
                .help("View \(stage.title). \(workspace.stageStatus(stage)).")
            }
        }
        .padding(.horizontal, 6).frame(height: 62).background(.bar)
    }
}
