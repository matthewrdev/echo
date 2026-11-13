import SwiftUI

struct WorkspaceModal: View {
    let sheet: WorkspaceSheet
    @ObservedObject var workspace: WorkspaceViewModel
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
        }.frame(width: 1100, height: 740)
            .modifier(ProjectEditConfirmation(workspace: workspace, active: true))
    }

    @ViewBuilder
    private var content: some View {
        switch sheet {
        case .photo: PhotoInspector(workspace: workspace)
        case .map: PhotoMapView(workspace: workspace, focusPhotoID: workspace.mapFocusPhotoID)
        case .settings: ProcessingSettingsView(workspace: workspace)
        case .georeference: GeoreferenceView(workspace: workspace)
        case .hints: HintEditor(workspace: workspace)
        case .history: RunHistoryView(workspace: workspace)
        }
    }
}
