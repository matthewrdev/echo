import SwiftUI

struct DroneImportPhotoList: View {
    @ObservedObject var model: DroneImportViewModel
    let photos: [DroneImportPhoto]
    var showsGroup = true

    var body: some View {
        if photos.isEmpty {
            ContentUnavailableView("No photos in this group", systemImage: "photo.on.rectangle")
        } else {
            List(photos, selection: $model.selection) { photo in
                HStack(spacing: 10) {
                    DronePhotoThumbnail(url: photo.id, width: 52, height: 38)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(photo.name)
                        if let date = photo.capturedAt {
                            Text(date.formatted(captureTimeStyle))
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    }
                    Spacer()
                    if photo.coordinate == nil {
                        Label("No GPS", systemImage: "location.slash")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if model.excluded.contains(photo.id) {
                        Text("Excluded").foregroundStyle(.secondary)
                    } else if showsGroup {
                        Text(model.name(of: model.assignments[photo.id])).foregroundStyle(.secondary)
                    }
                }
                .tag(photo.id)
            }
            .listStyle(.inset(alternatesRowBackgrounds: true))
        }
    }

    // EXIF capture timestamps have no required timezone. Display the original camera clock time.
    private var captureTimeStyle: Date.FormatStyle {
        var style = Date.FormatStyle(date: .abbreviated, time: .standard)
        if let cameraClock = TimeZone(secondsFromGMT: 0) { style.timeZone = cameraClock }
        return style
    }
}
