import SwiftUI

struct StageLogView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var expanded = true

    private var entries: [RunLogEntry] {
        workspace.logs.filter { $0.stage == workspace.selectedStage.rawValue || $0.stage == "run" }.suffix(25)
    }
    var body: some View {
        DisclosureGroup(isExpanded: $expanded) {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 5) {
                        if entries.isEmpty {
                            Text(
                                "\(workspace.stageStatus(workspace.selectedStage)). Messages will appear here when this stage runs."
                            )
                            .foregroundStyle(.secondary)
                        }
                        ForEach(entries) { entry in
                            HStack(alignment: .top, spacing: 12) {
                                Text(entry.time).monospacedDigit().foregroundStyle(.secondary).fixedSize()
                                Text(entry.message).textSelection(.enabled)
                            }.frame(maxWidth: .infinity, alignment: .leading).id(entry.id)
                        }
                    }.font(.caption).padding(.vertical, 6)
                }
                .frame(height: 100)
                .onChange(of: entries.last?.id) { _, id in if let id { proxy.scrollTo(id, anchor: .bottom) } }
            }
        } label: {
            Text("\(workspace.selectedStage.shortTitle) activity").font(.caption.weight(.semibold))
        }
        .padding(.horizontal, 16).padding(.vertical, 8)
    }
}
