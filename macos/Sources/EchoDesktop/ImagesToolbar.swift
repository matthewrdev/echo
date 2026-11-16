import SwiftUI

struct ImagesToolbar: ToolbarContent {
    @ObservedObject var workspace: WorkspaceViewModel

    var body: some ToolbarContent {
        ToolbarItemGroup(placement: .primaryAction) {
            Picker("Image view", selection: $workspace.imagesViewMode) {
                ForEach(ImagesViewMode.allCases) { mode in
                    Label(mode.rawValue, systemImage: mode.symbol).help(mode.help).tag(mode)
                }
            }
            .pickerStyle(.segmented).labelStyle(.iconOnly).help("Browse image tiles or photo locations")
            if workspace.imagesViewMode == .map {
                Button("Model + geographic export", systemImage: "globe") { workspace.sheet = .georeference }
                    .help("Model + geographic export")
            }
            Menu {
                Button("Trim similar photos", systemImage: "square.on.square") { workspace.trimSimilarPhotos() }
                    .disabled(!workspace.canEdit)
                    .help("Keep the sharpest of similar photos and discard extras. Originals can be restored.")
            } label: {
                Label("Image actions", systemImage: "ellipsis")
            }
            .menuIndicator(.hidden).accessibilityLabel("Image actions").help("Image actions")
            Button("Import photos", systemImage: "plus") { workspace.importPhotos() }
                .disabled(!workspace.canEdit)
                .help("Import photos")
        }
    }
}
