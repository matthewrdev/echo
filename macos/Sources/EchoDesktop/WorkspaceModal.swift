import SwiftUI

struct WorkspaceModal: View {
    let sheet: WorkspaceSheet
    @ObservedObject var workspace: WorkspaceViewModel
    let artifacts: ArtifactViewModel
    private var compact: Bool { sheet == .surveyExport || sheet == .printExport }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(sheet.title).font(.title3.bold())
                Spacer()
                Button("Done") { workspace.sheet = nil }.keyboardShortcut(.cancelAction)
            }.padding(16)
            Divider()
            GeometryReader { bounds in
                content.frame(width: bounds.size.width, height: bounds.size.height).clipped()
            }
        }.frame(width: compact ? 510 : 1100, height: compact ? 300 : 740)
            .modifier(ProjectEditConfirmation(workspace: workspace, active: true))
    }

    @ViewBuilder
    private var content: some View {
        switch sheet {
        case .photo: PhotoInspector(workspace: workspace, artifacts: artifacts)
        case .settings: ProcessingSettingsView(workspace: workspace)
        case .georeference: EmptyView()  // Presented inside the window by WorkspaceView.
        case .hints: HintEditor(workspace: workspace)
        case .storage: ArtifactStorageView(workspace: workspace)
        case .history: RunHistoryView(workspace: workspace)
        case .performance: PerformanceTraceView(workspace: workspace, model: workspace.performance)
        case .surveyExport: SurveyExportView(workspace: workspace)
        case .printExport: PrintMeshExportView(workspace: workspace)
        }
    }
}
