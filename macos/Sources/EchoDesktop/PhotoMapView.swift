import SwiftUI

struct PhotoMapView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @StateObject private var controller = CanvasViewportController()
    @State private var layer = PhotoMapLayer.satellite
    @State private var aligned = false
    @State private var distance = 20.0
    private let projector = MapCameraProjector()

    private var cameras: [MapCamera] {
        projector.cameras(
            photos: workspace.filteredPhotos, hints: workspace.inspection?.project.hints ?? [],
            reconstruction: workspace.inspection?.reconstruction, aligned: aligned, distance: distance)
    }

    var body: some View {
        let cameras = cameras
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                Picker("Positions", selection: $aligned) {
                    Text("Capture GPS + hints").tag(false)
                    Text("Aligned cameras").tag(true)
                }.labelsHidden().pickerStyle(.segmented).frame(width: 300)
                Spacer()
                Text("Frustum depth")
                Slider(value: $distance, in: 3...80).frame(width: 100)
                    .accessibilityLabel("Frustum depth")
                Text("\(Int(distance)) m").monospacedDigit()
            }.controlSize(.small)
                .help(
                    "Frustums show viewing direction and field of view. Their depth is a display scale, not measured ground coverage."
                )
            HStack(spacing: 12) {
                ForEach([-90, -45, 0, 45, 90], id: \.self) { angle in
                    HStack(spacing: 4) {
                        Circle().fill(Color(nsColor: CameraTiltPalette().color(pitch: Double(angle)))).frame(
                            width: 10, height: 10)
                        Text("\(angle)°").font(.caption)
                    }
                }
                Text("Down ← tilt → Up · Scroll to zoom · drag to pan").font(.caption).foregroundStyle(.secondary)
            }
            mapSurface(cameras)
            if let selected = cameras.first(where: { $0.id == workspace.selectedPhotoID }) {
                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(
                            "\(selected.photo.name) · \(selected.source) · tilt \(selected.pitchDegrees.map { $0.formatted(.number.precision(.fractionLength(1))) + "°" } ?? "unknown")"
                        ).font(.caption).lineLimit(1).truncationMode(.middle)
                        Text(
                            String(
                                format: "Latitude %.8f · Longitude %.8f", selected.position.latitude,
                                selected.position.longitude)
                        )
                        .font(.caption).textSelection(.enabled)
                    }
                    Button("Copy coordinates", systemImage: "doc.on.doc") {
                        workspace.copyText(
                            String(format: "%.8f, %.8f", selected.position.latitude, selected.position.longitude))
                    }
                    .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).help("Copy coordinates")
                }
            }
        }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var mapIdentity: String {
        workspace.root + String(aligned)
    }

    private func mapSurface(_ cameras: [MapCamera]) -> some View {
        Group {
            if cameras.isEmpty {
                ContentUnavailableView(
                    "No located photos", systemImage: "location.slash",
                    description: Text(
                        "Try another image filter or add camera position hints for photographs without GPS."))
            } else if layer != .offline {
                NativePhotoMap(
                    cameras: cameras, identity: mapIdentity,
                    satellite: layer == .satellite, selected: $workspace.selectedPhotoID)
            } else {
                VStack(spacing: 8) {
                    CanvasControls(controller: controller, showsInstructions: false)
                        .padding(.trailing, 56)
                    CanvasViewport(
                        renderer: PhotoMapRenderer(
                            identity: mapIdentity, cameras: cameras, selected: workspace.selectedPhotoID,
                            selection: { workspace.selectedPhotoID = $0 }), controller: controller)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .overlay(alignment: .bottomTrailing) {
            PhotoMapLayerControls(layer: $layer).padding(12)
        }
    }
}
