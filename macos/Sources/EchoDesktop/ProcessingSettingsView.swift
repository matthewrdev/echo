import SwiftUI

struct ProcessingSettingsView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var draft = ProcessingSettings()
    @State private var stage: PipelineStage

    init(workspace: WorkspaceViewModel) {
        self.workspace = workspace
        _stage = State(initialValue: workspace.selectedStage)
    }

    var body: some View {
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 5) {
                ForEach(PipelineStage.allCases) { item in
                    Button {
                        stage = item
                    } label: {
                        Text(item.title).font(.callout).frame(maxWidth: .infinity, alignment: .leading).padding(10)
                            .background(
                                stage == item ? Color.accentColor.opacity(0.15) : Color.clear,
                                in: RoundedRectangle(cornerRadius: 6)
                            )
                            .contentShape(Rectangle())
                    }.buttonStyle(.plain)
                }
                Spacer()
            }.padding(12).frame(width: 220)
            Divider()
            VStack(alignment: .leading, spacing: 12) {
                Text(stage.title).font(.title2.bold())
                Text("Next run: \(draft.summary(for: stage))").font(.callout)
                if let saved = workspace.inspection?.run?.snapshot.settings {
                    Text("Viewed run used: \(saved.summary(for: stage))").font(.caption).foregroundStyle(.secondary)
                }
                StageSettingsEditor(
                    stage: stage, settings: $draft, imageCount: workspace.inspection?.project.images.count ?? 0)
                if let current = workspace.inspection?.project.settings,
                    let changed = ProcessingImpact().firstChangedStage(from: current, to: draft)
                {
                    Label(ProcessingImpact().warning(from: changed), systemImage: "exclamationmark.triangle.fill")
                        .font(.callout).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                }
                Divider()
                HStack {
                    Text("Changes apply to new runs. Resume keeps the saved run’s settings.")
                        .font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button(workspace.busy ? "Saving…" : "Save project settings") { workspace.save(settings: draft) }
                        .buttonStyle(.borderedProminent).disabled(!workspace.canEdit)
                }
                if workspace.status == "Processing settings saved." {
                    Label(workspace.status, systemImage: "checkmark.circle").font(.caption).foregroundStyle(.green)
                }
            }.padding(20).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .onAppear { if let settings = workspace.inspection?.project.settings { draft = settings } }
    }
}
