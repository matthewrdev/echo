import SwiftUI

struct PhotoMapView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @StateObject private var controller = CanvasViewportController()
    @State private var layer = PhotoMapLayer.satellite
    @State private var distance = 20.0
    @State private var boxed: Set<String> = []
    private let projector = MapCameraProjector()

    private var cameras: [MapCamera] {
        projector.cameras(
            photos: workspace.filteredPhotos, hints: workspace.inspection?.project.hints ?? [],
            reconstruction: nil, aligned: false, distance: distance)
    }

    var body: some View {
        let cameras = cameras
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 12) {
                ForEach([-90, -45, 0, 45, 90], id: \.self) { angle in
                    HStack(spacing: 4) {
                        Circle().fill(Color(nsColor: CameraTiltPalette().color(pitch: Double(angle)))).frame(
                            width: 10, height: 10)
                        Text("\(angle)°").font(.caption)
                    }
                }
                Text("Down ← tilt → Up · Scroll to zoom · Drag to pan · Shift-drag to select")
                    .font(.caption).foregroundStyle(.secondary)
            }
            mapSurface(cameras)
            let selected = cameras.map(\.photo).filter { boxed.contains($0.id) }
            if !selected.isEmpty {
                selectionActions(selected)
            } else if let selected = cameras.first(where: { $0.id == workspace.selectedPhotoID }) {
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
        }
        .onChange(of: cameras.map(\.id)) { _, visible in boxed.formIntersection(visible) }
        .onChange(of: workspace.root) { _, _ in boxed.removeAll() }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var mapIdentity: String {
        workspace.root
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
                    satellite: layer == .satellite, selected: $workspace.selectedPhotoID,
                    boxed: boxed, boxSelection: addToSelection, singleSelection: selectOne)
            } else {
                VStack(spacing: 8) {
                    CanvasControls(controller: controller, showsInstructions: false)
                        .padding(.trailing, 56)
                    let renderer = PhotoMapRenderer(
                        identity: mapIdentity, cameras: cameras, selected: workspace.selectedPhotoID,
                        boxed: boxed, selection: selectOne)
                    CanvasViewport(renderer: renderer, controller: controller) { rect in
                        addToSelection(renderer.cameraIDs(in: rect))
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .overlay(alignment: .bottomTrailing) {
            VStack(spacing: 6) {
                PhotoMapFrustumControl(distance: $distance)
                    .disabled(cameras.isEmpty)
                PhotoMapLayerControls(layer: $layer)
            }.padding(12)
        }
    }

    private func selectOne(_ id: String?) {
        boxed.removeAll()
        workspace.selectedPhotoID = id
    }

    private func addToSelection(_ ids: Set<String>) {
        guard !ids.isEmpty else { return }
        boxed.formUnion(ids)
        if let id = workspace.selectedPhotoID, cameras.contains(where: { $0.id == id }) {
            boxed.insert(id)
        }
        workspace.selectedPhotoID = nil
    }

    private func selectionActions(_ photos: [Photo]) -> some View {
        HStack {
            Text(photos.count == 1 ? "1 photo selected" : "\(photos.count.formatted()) photos selected")
                .font(.caption)
            Spacer()
            Button("Exclude from processing") { workspace.excludePhotos(photos) }
                .disabled(!workspace.canEdit || !photos.contains(where: \.enabled))
            Button("Delete photos…", role: .destructive) { workspace.deletePhotos(photos) }
                .disabled(!workspace.canEdit)
            Button("Clear selection") { selectOne(nil) }
        }.controlSize(.small)
    }
}
