import SwiftUI

struct GpsPolygonEditor: View {
    let polygons: [GpsPolygon]
    let canEdit: Bool
    @Binding var draft: GpsPolygon?
    let save: ([GpsPolygon]) -> Void

    var body: some View {
        HStack(spacing: 8) {
            if let polygon = draft {
                Text("Click the map to add corners · \(polygon.vertices.count) vertices")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Picker(
                    "Mode",
                    selection: Binding(get: { polygon.mode }, set: { draft?.mode = $0 })
                ) {
                    ForEach(CropRegionMode.allCases, id: \.self) { mode in
                        Text(mode.title).tag(mode)
                    }
                }
                .fixedSize()
                Button("Undo vertex") { _ = draft?.vertices.popLast() }
                    .disabled(polygon.vertices.isEmpty)
                if polygons.contains(where: { $0.id == polygon.id }) {
                    Button("Clear vertices") { draft?.vertices.removeAll() }
                        .disabled(polygon.vertices.isEmpty)
                    Button("Remove polygon", role: .destructive) {
                        save(polygons.filter { $0.id != polygon.id })
                    }
                }
                Button("Cancel") { draft = nil }
                Button("Save polygon") { save(replacing(polygon)) }
                    .disabled(polygon.vertices.count < 3)
                    .buttonStyle(.borderedProminent)
            } else {
                Text("GPS crop polygons")
                    .font(.caption.weight(.semibold))
                if !polygons.isEmpty {
                    Menu("\(polygons.count) saved") {
                        ForEach(Array(polygons.enumerated()), id: \.element.id) { index, polygon in
                            Button("\(polygon.mode.title) polygon \(index + 1)") { draft = polygon }
                        }
                    }
                    .fixedSize()
                }
                Spacer()
                Menu("Draw polygon") {
                    ForEach(CropRegionMode.allCases, id: \.self) { mode in
                        Button(mode.title) { draft = GpsPolygon(mode: mode) }
                    }
                }
                .fixedSize()
            }
        }
        .controlSize(.small)
        .disabled(!canEdit)
    }

    private func replacing(_ polygon: GpsPolygon) -> [GpsPolygon] {
        guard let index = polygons.firstIndex(where: { $0.id == polygon.id }) else {
            return polygons + [polygon]
        }
        var updated = polygons
        updated[index] = polygon
        return updated
    }
}
