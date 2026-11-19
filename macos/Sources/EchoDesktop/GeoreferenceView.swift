import SwiftUI

struct GeoreferenceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @Binding var expanded: Bool
    @StateObject private var model = GeoreferenceViewModel()
    @StateObject private var probe = GeographicProbeViewModel()
    @StateObject private var controller = SceneViewportController()
    @State private var tab = "Map"
    @State private var opacity = 0.8
    @State private var showCameras = true
    @State private var showModel = true
    @State private var showGround = false
    @State private var satellite = true
    @State private var fit = UUID()

    private var identity: String {
        workspace.root + (workspace.geographicRun?.id ?? "")
            + (workspace.geographicRun?.artifacts["alignment"] ?? "")
            + (workspace.geographicRun?.artifacts["mesh"] ?? "")
            + (workspace.geographicRun?.artifacts["texture"] ?? "")
            + (workspace.geographicRun?.artifacts["export"] ?? "")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("Georeference").font(.headline)
                Picker("Geographic view", selection: $tab) {
                    Text("Map").tag("Map")
                    Text("3D").tag("3D")
                    Text("Test GPS").tag("Test GPS")
                    Text("Details").tag("Details")
                }.labelsHidden().pickerStyle(.segmented).frame(width: 340)
                Spacer()
                Button("Export geographic data…", systemImage: "square.and.arrow.up") { export() }
                    .labelStyle(.iconOnly).help("Export geographic data for GIS and Redpoint")
                    .disabled(model.artifact == nil || model.loading || model.exporting)
                Button(
                    expanded ? "Restore viewer size" : "Fill window",
                    systemImage: expanded ? "arrow.down.right.and.arrow.up.left" : "arrow.up.left.and.arrow.down.right"
                ) { expanded.toggle() }
                .labelStyle(.iconOnly).help(expanded ? "Restore viewer size" : "Fill window")
                Button("Close", systemImage: "xmark") { workspace.sheet = nil }
                    .labelStyle(.iconOnly).help("Close geographic viewer").keyboardShortcut(.cancelAction)
            }
            if model.artifact != nil {
                Text(referenceSummary).font(.caption).foregroundStyle(.secondary)
                    .lineLimit(1).textSelection(.enabled)
                    .help(
                        "Photo altitudes and their reference evidence are retained in the export. Geographic accuracy depends on GPS and placement hints."
                    )
            }
            if model.exporting { ProgressView("Exporting geographic data…").controlSize(.small) }
            if let message = model.exportMessage { Text(message).font(.caption).textSelection(.enabled) }
            if tab == "Details" {
                GeoreferenceDetailsView(workspace: workspace, reconstruction: model.artifact?.reconstruction)
            } else if model.loading {
                ProgressView("Preparing geographic model…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let artifact = model.artifact {
                if tab == "Map" {
                    map(artifact)
                } else if tab == "Test GPS" {
                    GeoreferenceProbeView(
                        artifact: artifact, probe: probe, controller: controller, showGround: $showGround)
                } else {
                    modelView(artifact)
                }
            } else {
                ContentUnavailableView(
                    "Geographic view unavailable", systemImage: "globe",
                    description: Text(model.error ?? "Complete alignment to view geographic placement.")
                )
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .task(id: identity) {
            probe.clear(in: model.artifact)
            await model.load(root: workspace.root, run: workspace.geographicRun)
        }
        .onDisappear { model.cancel() }
    }

    private var referenceSummary: String {
        guard let origin = model.artifact?.reconstruction.origin else { return "" }
        let photos = workspace.geographicRun?.snapshot.images ?? []
        let count = photos.filter { $0.gps?.altitude != nil }.count
        return String(format: "WGS84 · %.6f, %.6f", origin.latitude, origin.longitude)
            + " · Altitude \(count)/\(photos.count) photos · Reference: \(origin.altitudeDatum ?? "unknown")"
    }

    private func map(_ artifact: GeoreferenceArtifact) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Toggle("Satellite", isOn: $satellite)
                Toggle("Model", isOn: $showModel).disabled(artifact.overlay == nil)
                Toggle("Cameras", isOn: $showCameras).help("Camera positions and viewing directions")
                Text("Opacity")
                Slider(value: $opacity, in: 0...1).frame(width: 120).disabled(artifact.overlay == nil)
                Spacer()
                Button("Fit view") { fit = UUID() }
            }.font(.callout)
            NativePhotoMap(
                cameras: showCameras ? artifact.cameras : [], identity: identity + fit.uuidString,
                satellite: satellite, selected: $workspace.selectedPhotoID,
                modelOverlay: showModel ? artifact.overlay : nil, modelOpacity: opacity)
            if artifact.overlay == nil {
                Text("Process through Mesh to add the model.").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func modelView(_ artifact: GeoreferenceArtifact) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Map display height")
                TextField("Local up", value: $model.mapHeight, format: .number.precision(.fractionLength(2)))
                    .frame(width: 90).onSubmit { model.updateMapHeight() }
                Text("m · display only")
                    .foregroundStyle(.secondary)
                Spacer()
                if model.mapLoading { ProgressView().controlSize(.small) }
                if model.mapError != nil {
                    Button("Retry imagery") { Task { await model.loadMap() } }
                }
            }.font(.caption)
            if let error = model.mapError { Text(error).font(.caption).foregroundStyle(.orange) }
            Artifact3DViewer(
                presentation: artifact.presentation, openFeatures: { _ in }, controller: controller,
                showGround: $showGround)
            Text("Apple Maps · Flat imagery")
                .font(.caption).foregroundStyle(.secondary)
        }
        .task(id: artifact.presentation.id) { await model.loadMap() }
    }

    private func export() {
        guard let run = workspace.geographicRun, let artifact = model.artifact else { return }
        let panel = NSSavePanel()
        panel.title = "Export geographic data for GIS and Redpoint"
        panel.prompt = "Export"
        panel.nameFieldStringValue = "Echo-GIS-\(run.id.prefix(8))"
        panel.message = "Exports GeoPackage, GeoJSON, GeoTIFF and available Redpoint, 3D Tiles and LAS data."
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let destination = panel.url else { return }
        model.exporting = true
        model.exportMessage = nil
        let root = workspace.root
        Task {
            defer { model.exporting = false }
            do {
                try await workspace.exportGeoreference(root: root, runID: run.id, destination: destination)
                if let overlay = artifact.overlay {
                    do {
                        try GeographicRasterWriter().write(overlay, directory: destination)
                    } catch {
                        model.exportMessage =
                            "GIS geometry exported, but the model image failed: \(error.localizedDescription). Folder: \(destination.path)"
                        NSWorkspace.shared.open(destination)
                        return
                    }
                }
                model.exportMessage =
                    "Exported to \(destination.path). Open QGIS.txt for instructions and export-manifest.json for available formats."
                NSWorkspace.shared.open(destination)
            } catch {
                model.exportMessage = "Export failed: \(error.localizedDescription)"
            }
        }
    }
}
