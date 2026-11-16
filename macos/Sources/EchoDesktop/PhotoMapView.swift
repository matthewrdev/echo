import MapKit
import SwiftUI

struct PhotoMapView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @StateObject private var controller = CanvasViewportController()
    @State private var selectedOnly: Bool
    @State private var layer = PhotoMapLayer.satellite
    @State private var aligned = false
    @State private var showAll = true
    @State private var distance = 20.0
    private let projector = MapCameraProjector()

    init(workspace: WorkspaceViewModel, focusPhotoID: String? = nil) {
        self.workspace = workspace
        _selectedOnly = State(initialValue: focusPhotoID != nil)
    }

    private var cameras: [MapCamera] {
        projector.cameras(
            photos: selectedOnly
                ? workspace.inspection?.project.images.filter { $0.id == workspace.selectedPhotoID } ?? []
                : workspace.filteredPhotos, hints: workspace.inspection?.project.hints ?? [],
            reconstruction: workspace.inspection?.reconstruction, aligned: aligned, distance: distance)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Picker("Positions", selection: $aligned) {
                    Text("Capture GPS + hints").tag(false)
                    Text("Aligned cameras").tag(true)
                }.pickerStyle(.segmented).frame(width: 340)
                Toggle("Selected photo only", isOn: $selectedOnly).disabled(workspace.selectedPhoto == nil)
                Toggle("All frustums", isOn: $showAll)
                Spacer()
                Button("Model + geographic export") { workspace.sheet = .georeference }
                    .disabled(workspace.inspection?.run?.artifacts["alignment"] == nil)
            }
            HStack {
                Text("Frustum depth")
                Slider(value: $distance, in: 3...80).frame(width: 120)
                Text("\(Int(distance)) m").monospacedDigit()
                Spacer()
            }
            Text(
                "N ↑ · \(cameras.count) located photos · \(cameras.filter { $0.corners.isEmpty }.count) without orientation. Frustums show viewing direction and field of view; their depth is a display scale, not measured ground coverage."
            )
            .font(.caption).foregroundStyle(.secondary)
            HStack {
                Picker("Map", selection: $layer) {
                    ForEach(PhotoMapLayer.allCases) { Text($0.rawValue).tag($0) }
                }.pickerStyle(.segmented).frame(width: 340)
                Text(layer == .offline ? "Offline location grid" : "Apple Maps")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
            }
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
            if cameras.isEmpty {
                ContentUnavailableView(
                    "No located photos", systemImage: "location.slash",
                    description: Text("Add camera position hints to place photographs without GPS."))
            } else if layer != .offline {
                NativePhotoMap(
                    cameras: cameras, identity: workspace.root + String(aligned) + String(selectedOnly),
                    satellite: layer == .satellite, showAll: showAll, selected: $workspace.selectedPhotoID)
            } else {
                CanvasControls(controller: controller)
                CanvasViewport(
                    renderer: PhotoMapRenderer(
                        identity: workspace.root + String(aligned) + String(selectedOnly),
                        cameras: cameras, selected: workspace.selectedPhotoID, showAll: showAll,
                        selection: { workspace.selectedPhotoID = $0 }), controller: controller)
            }
            if let selected = cameras.first(where: { $0.id == workspace.selectedPhotoID }) {
                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(
                            "\(selected.photo.name) · \(selected.source) · tilt \(selected.pitchDegrees.map { $0.formatted(.number.precision(.fractionLength(1))) + "°" } ?? "unknown")"
                        ).font(.caption)
                        Text(
                            String(
                                format: "Latitude %.8f · Longitude %.8f", selected.position.latitude,
                                selected.position.longitude)
                        )
                        .font(.caption).textSelection(.enabled)
                    }
                    Button("Copy coordinates") {
                        workspace.copyText(
                            String(format: "%.8f, %.8f", selected.position.latitude, selected.position.longitude))
                    }.controlSize(.small)
                    Spacer()
                    Button("Open photograph & metadata") { workspace.sheet = .photo }
                }
            }
        }.padding(18)
    }

    private func coordinate(_ position: GeoPosition) -> CLLocationCoordinate2D {
        CLLocationCoordinate2D(latitude: position.latitude, longitude: position.longitude)
    }
}
