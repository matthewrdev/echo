import SwiftUI

struct DroneImportMapView: View {
    @ObservedObject var model: DroneImportViewModel

    var body: some View {
        let photos = model.editablePhotos
        let groupIndices = Dictionary(
            uniqueKeysWithValues: model.groups.enumerated().map { ($0.element.id, $0.offset) })
        let appearances = Dictionary(
            uniqueKeysWithValues: photos.map { photo in
                let group = model.assignments[photo.id].flatMap { groupIndices[$0] }
                return (
                    photo.id,
                    DroneMapAppearance(
                        group: group, excluded: model.excluded.contains(photo.id),
                        selected: model.selection.contains(photo.id))
                )
            })
        let located = photos.count { $0.coordinate != nil }
        if located == 0 {
            ContentUnavailableView(
                "No geotagged photos", systemImage: "location.slash",
                description: Text("Use Photos or Groups to assign these images."))
        } else {
            HStack(spacing: 0) {
                DroneImportMap(photos: photos, appearances: appearances, selection: $model.selection)
                Divider()
                VStack(alignment: .leading, spacing: 12) {
                    Text("Click a photo or hold Shift and drag to select an area.")
                        .font(.callout).foregroundStyle(.secondary)
                    if photos.count > located {
                        Text("\((photos.count - located).formatted()) without GPS").font(.caption)
                    }
                    if let photo = photos.first(where: { model.selection.contains($0.id) }) {
                        DronePhotoThumbnail(url: photo.id, width: 202, height: 140).id(photo.id)
                        Text(photo.name).font(.headline).lineLimit(1)
                        Text(
                            model.excluded.contains(photo.id) ? "Excluded" : model.name(of: model.assignments[photo.id])
                        )
                        .font(.caption).foregroundStyle(.secondary)
                    }
                    Divider()
                    ScrollView {
                        VStack(alignment: .leading, spacing: 8) {
                            ForEach(model.groups) { group in
                                legend(
                                    group.name,
                                    index: groupIndices[group.id], excluded: false)
                            }
                            legend("Unassigned", index: nil, excluded: false)
                            legend("Excluded", index: nil, excluded: true)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(12)
                .frame(width: 226)
            }
        }
    }

    private func legend(_ name: String, index: Int?, excluded: Bool) -> some View {
        let appearance = DroneMapAppearance(group: index, excluded: excluded, selected: false)
        return HStack {
            Circle().fill(Color(nsColor: appearance.color)).frame(width: 9, height: 9)
                .opacity(excluded ? 0.38 : 1)
            Text(name).lineLimit(1)
        }
        .font(.caption)
    }
}
