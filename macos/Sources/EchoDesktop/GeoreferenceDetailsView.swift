import SwiftUI

struct GeoreferenceDetailsView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    var reconstruction: ReconstructionScene? = nil
    @State private var sidecar = ""

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Georeference").font(.title.bold())
                if let scene = reconstruction ?? workspace.inspection?.reconstruction {
                    Text(scene.frame)
                    if let origin = scene.origin {
                        Text(
                            "Origin: \(origin.latitude), \(origin.longitude) · altitude \(origin.altitude.map { String($0) } ?? "unknown") m"
                        )
                        Text("Altitude datum: \(origin.altitudeDatum ?? "unknown")").foregroundStyle(.secondary)
                    }
                    ForEach(scene.warnings, id: \.self) { Text($0).foregroundStyle(.orange) }
                }
                if let photo = workspace.selectedPhoto {
                    Text(photo.name).font(.headline)
                    Grid(alignment: .leading, horizontalSpacing: 20, verticalSpacing: 6) {
                        ForEach(photo.metadata.keys.sorted(), id: \.self) { key in
                            GridRow {
                                Text(key).foregroundStyle(.secondary)
                                Text(photo.metadata[key]?.description ?? "").textSelection(.enabled)
                            }
                        }
                    }
                }
                if !sidecar.isEmpty {
                    DisclosureGroup("Export coordinate transform and camera data") {
                        Text(sidecar).font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading).padding(24)
        }
        .task(id: workspace.geographicRun?.artifacts["export"]) {
            guard let path = workspace.geographicRun?.artifacts["export"] else {
                sidecar = ""
                return
            }
            sidecar =
                (try? String(
                    contentsOf: URL(fileURLWithPath: workspace.root).appendingPathComponent(path)
                        .appendingPathComponent("cliff.geo.json"), encoding: .utf8)) ?? ""
        }
    }
}
