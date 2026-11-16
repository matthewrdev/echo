import SwiftUI

struct PhotoTile: View {
    let photo: Photo
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var thumbnail: NSImage?

    private var selected: Bool { workspace.selectedPhotoID == photo.id }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Rectangle().fill(.quaternary)
                .aspectRatio(4 / 3, contentMode: .fit)
                .overlay {
                    if let thumbnail {
                        Image(nsImage: thumbnail).resizable().scaledToFit()
                    } else {
                        Image(systemName: "photo").font(.largeTitle).foregroundStyle(.tertiary)
                    }
                }
                .overlay(alignment: .topTrailing) {
                    if photo.isDiscarded || !photo.enabled {
                        Label(photo.isDiscarded ? "Discarded" : "Excluded", systemImage: "archivebox")
                            .font(.caption2).padding(5).background(.regularMaterial, in: Capsule()).padding(8)
                    }
                }
                .clipped()
            VStack(alignment: .leading, spacing: 5) {
                Text(photo.name).font(.caption.weight(.medium)).lineLimit(1).truncationMode(.middle)
                HStack {
                    Text(photo.camera).lineLimit(1)
                    Spacer(minLength: 2)
                    if photo.gps != nil { Image(systemName: "location.fill").help("GPS present") }
                    Text("\(photo.width) × \(photo.height)").lineLimit(1).minimumScaleFactor(0.8)
                }.font(.caption2).foregroundStyle(.secondary)
            }.padding(10)
        }
        .background(selected ? Color.accentColor.opacity(0.12) : Color(nsColor: .controlBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .overlay {
            RoundedRectangle(cornerRadius: 8)
                .strokeBorder(selected ? Color.accentColor : Color.primary.opacity(0.1), lineWidth: selected ? 2 : 1)
        }
        .contentShape(Rectangle())
        .onTapGesture(count: 2, perform: openPhoto)
        .onTapGesture { workspace.selectedPhotoID = photo.id }
        .help(photo.hoverSummary)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(
            "\(photo.name), \(photo.camera), \(photo.isDiscarded ? "Discarded" : photo.enabled ? "Enabled" : "Excluded")"
        )
        .accessibilityAddTraits(selected ? [.isButton, .isSelected] : .isButton)
        .accessibilityAction { workspace.selectedPhotoID = photo.id }
        .accessibilityAction(named: "Open photograph & metadata", openPhoto)
        .contextMenu {
            Button("Open photograph & metadata", action: openPhoto)
            Button("Show on map") { workspace.openMap(for: photo) }.disabled(photo.gps == nil)
            Button(
                photo.isDiscarded
                    ? "Restore photo" : photo.enabled ? "Exclude from processing" : "Include in processing"
            ) {
                workspace.setEnabled(photo, enabled: !photo.enabled)
            }.disabled(!workspace.canEdit)
        }
        .task(id: workspace.root + photo.preview) {
            thumbnail = nil
            let path = URL(fileURLWithPath: workspace.root).appendingPathComponent(photo.preview)
            let loaded = await Task.detached(priority: .utility) { NSImage(contentsOf: path) }.value
            guard !Task.isCancelled else { return }
            thumbnail = loaded
        }
    }

    private func openPhoto() {
        workspace.selectedPhotoID = photo.id
        workspace.sheet = .photo
    }
}
