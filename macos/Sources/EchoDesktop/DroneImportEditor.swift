import SwiftUI

struct DroneImportEditor: View {
    @ObservedObject var model: DroneImportViewModel
    @State private var tab = DroneImportTab.photos
    @State private var groupID: UUID?

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Picker("View", selection: $tab) {
                    ForEach(DroneImportTab.allCases) { item in Text(item.rawValue).tag(item) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 240)
                Spacer()
                Text("\(model.selection.count.formatted()) selected")
                    .foregroundStyle(.secondary)
                Menu("Selection") {
                    Button("Select All Photos") { model.selection = Set(model.editablePhotos.map(\.id)) }
                    Button("Clear Selection") { model.selection = [] }
                        .disabled(model.selection.isEmpty)
                    Divider()
                    Button("Exclude from Import") { model.setSelectionExcluded(true) }
                        .disabled(model.selection.subtracting(model.excluded).isEmpty)
                    Button("Include in Import") { model.setSelectionExcluded(false) }
                        .disabled(model.selection.isDisjoint(with: model.excluded))
                }
                Menu("Move to Group") {
                    ForEach(model.groups.filter { group in !model.projects.contains { $0.id == group.id } }) { group in
                        Button(group.name) { model.assignSelection(to: group.id) }
                    }
                    Button("Unassigned") { model.assignSelection(to: nil) }
                }
                .disabled(model.selection.isEmpty)
                Button("New Group") {
                    groupID = model.addGroup()
                    tab = .groups
                }
            }
            .padding(12)
            Divider()
            switch tab {
            case .photos:
                DroneImportPhotoList(model: model, photos: model.editablePhotos)
            case .groups:
                DroneImportGroupsView(model: model, groupID: $groupID)
            case .map:
                DroneImportMapView(model: model)
            }
        }
    }
}
