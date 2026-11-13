import SwiftUI

struct ArtifactUnitRow: View {
    let unit: ArtifactUnit
    let selected: Bool
    @State private var images: [NSImage] = []

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(spacing: 3) {
                ForEach(images.indices, id: \.self) { index in
                    Image(nsImage: images[index]).resizable().scaledToFill()
                        .frame(width: images.count > 1 ? 81 : 166, height: 70).clipped()
                }
            }.clipShape(RoundedRectangle(cornerRadius: 4))
            Text(unit.title).font(.caption).lineLimit(3)
            if !unit.detail.isEmpty { Text(unit.detail).font(.caption2).foregroundStyle(.secondary) }
        }
        .frame(maxWidth: .infinity, alignment: .leading).padding(7)
        .background(
            selected ? Color.accentColor.opacity(0.18) : Color.secondary.opacity(0.06),
            in: RoundedRectangle(cornerRadius: 6)
        )
        .contentShape(Rectangle())
        .help(unit.title).accessibilityLabel(unit.title + ", " + unit.detail)
        .task(id: unit.id) {
            images = await Task.detached(priority: .utility) { unit.thumbnails.compactMap { NSImage(contentsOf: $0) } }
                .value
        }
    }
}
