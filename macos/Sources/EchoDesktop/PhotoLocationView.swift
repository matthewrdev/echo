import SwiftUI

struct PhotoLocationView: View {
    let photo: Photo
    let copy: (String) -> Void
    let showMap: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label("Capture location", systemImage: "location.fill").font(.headline)
            if let gps = photo.gps {
                MetadataValueRow(
                    entry: MetadataEntry(key: "Latitude", value: String(format: "%.8f", gps.latitude)), copy: copy)
                MetadataValueRow(
                    entry: MetadataEntry(key: "Longitude", value: String(format: "%.8f", gps.longitude)), copy: copy)
                if let altitude = gps.altitude {
                    Text("Altitude: \(altitude.formatted()) m · \(gps.altitudeDatum ?? "datum unknown")")
                        .font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
                }
                HStack {
                    Button("Show on map", systemImage: "map", action: showMap)
                    Button("Copy coordinates") { copy(String(format: "%.8f, %.8f", gps.latitude, gps.longitude)) }
                }.controlSize(.small)
            } else {
                Text("No capture GPS in this photograph. Add a placement hint to position it.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(12).background(.quaternary.opacity(0.4), in: RoundedRectangle(cornerRadius: 8))
    }
}
