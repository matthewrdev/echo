import SwiftUI

struct PhotoThumbnailRow: View {
    let photo: Photo
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var thumbnail: NSImage?

    var body: some View {
        HStack(spacing: 10) {
            Group {
                if let thumbnail {
                    Image(nsImage: thumbnail).resizable().scaledToFill()
                } else {
                    Rectangle().fill(.quaternary).overlay { Image(systemName: "photo") }
                }
            }
            .frame(width: 76, height: 56).clipped().clipShape(RoundedRectangle(cornerRadius: 5))
            VStack(alignment: .leading, spacing: 4) {
                Text(photo.name).font(.caption.weight(.medium)).lineLimit(2)
                Text(photo.camera).font(.caption2).foregroundStyle(.secondary)
                HStack(spacing: 5) {
                    if photo.gps != nil { Image(systemName: "location.fill") }
                    if photo.isDiscarded {
                        Label("Discarded", systemImage: "archivebox")
                    } else if !photo.enabled {
                        Text("Excluded")
                    }
                    Text("\(photo.width) × \(photo.height)")
                }.font(.caption2).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
        .padding(6)
        .background(
            workspace.selectedPhotoID == photo.id ? Color.accentColor.opacity(0.16) : Color.clear,
            in: RoundedRectangle(cornerRadius: 7)
        )
        .contentShape(Rectangle())
        .onTapGesture(count: 2) {
            workspace.selectedPhotoID = photo.id
            workspace.sheet = .photo
        }
        .onTapGesture { workspace.selectedPhotoID = photo.id }
        .help(photo.hoverSummary)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(
            "\(photo.name), \(photo.camera), \(photo.isDiscarded ? "Discarded, " : "")double click to open photograph and metadata"
        )
        .contextMenu {
            Button("Open photograph & metadata") {
                workspace.selectedPhotoID = photo.id
                workspace.sheet = .photo
            }
            Button(
                photo.isDiscarded
                    ? "Restore photo" : (photo.enabled ? "Exclude from processing" : "Include in processing")
            ) {
                workspace.setEnabled(photo, enabled: !photo.enabled)
            }.disabled(!workspace.canEdit)
        }
        .task(id: workspace.root + photo.preview) {
            let path = URL(fileURLWithPath: workspace.root).appendingPathComponent(photo.preview)
            thumbnail = await Task.detached(priority: .utility) { NSImage(contentsOf: path) }.value
        }
    }
}
