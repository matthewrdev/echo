import SwiftUI

struct DroneImportGroupsView: View {
    @ObservedObject var model: DroneImportViewModel
    @Binding var groupID: UUID?

    var body: some View {
        let photos = Dictionary(grouping: model.editablePhotos) { model.assignments[$0.id] }
        let groups = model.groups.filter { group in !model.projects.contains { $0.id == group.id } }
        HStack(spacing: 0) {
            ScrollView {
                VStack(spacing: 5) {
                    groupRow("Unassigned", count: photos[nil]?.count ?? 0, id: nil)
                    ForEach(groups) { group in
                        groupRow(group.name, count: photos[group.id]?.count ?? 0, id: group.id)
                    }
                }
                .padding(8)
            }
            .frame(width: 220)
            Divider()
            VStack(spacing: 0) {
                let shown = photos[groupID] ?? []
                HStack {
                    if let groupID {
                        TextField(
                            "Group name",
                            text: Binding(get: { model.name(of: groupID) }, set: { model.rename(groupID, to: $0) })
                        )
                        .textFieldStyle(.roundedBorder)
                        .accessibilityLabel("Group name")
                    } else {
                        Text("Unassigned").font(.headline)
                    }
                    Spacer()
                    Button("Select Group") { model.selection = Set(shown.map(\.id)) }
                        .disabled(shown.isEmpty)
                }
                .padding(10)
                Divider()
                DroneImportPhotoList(model: model, photos: shown, showsGroup: false)
            }
        }
        .onChange(of: model.projects.count) {
            if model.projects.contains(where: { $0.id == groupID }) { groupID = nil }
        }
    }

    private func groupRow(_ name: String, count: Int, id: UUID?) -> some View {
        Button {
            groupID = id
        } label: {
            HStack {
                Text(name.isEmpty ? "Untitled group" : name).lineLimit(1)
                Spacer()
                Text(count.formatted()).monospacedDigit().foregroundStyle(.secondary)
            }
            .padding(8)
            .background(
                groupID == id ? Color.accentColor.opacity(0.16) : Color.clear,
                in: RoundedRectangle(cornerRadius: 5)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

}
