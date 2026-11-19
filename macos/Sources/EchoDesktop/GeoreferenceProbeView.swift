import SwiftUI

struct GeoreferenceProbeView: View {
    let artifact: GeoreferenceArtifact
    @ObservedObject var probe: GeographicProbeViewModel
    @ObservedObject var controller: SceneViewportController
    @Binding var showGround: Bool
    @State private var selectedCamera: String?
    @State private var satellite = true

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .bottom, spacing: 12) {
                field("Latitude", text: $probe.latitude, width: 130)
                field("Longitude", text: $probe.longitude, width: 130)
                field("Altitude (optional, m)", text: $probe.altitude, width: 140)
                field("Accuracy radius (m)", text: $probe.accuracy, width: 120)
                Button("Show location") { probe.show(in: artifact) }
                    .keyboardShortcut(.return, modifiers: .command)
                Button("Clear") { probe.clear(in: artifact) }.disabled(probe.result == nil && probe.error == nil)
                Spacer()
            }
            Text(
                "Leave altitude blank for the topmost surface. Entered altitude must use the model's reference: \(artifact.reconstruction.origin?.altitudeDatum ?? "unknown")."
            )
            .font(.caption).foregroundStyle(.secondary)
            if let error = probe.error { Text(error).font(.callout).foregroundStyle(.orange).textSelection(.enabled) }
            if let result = probe.result {
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(String(format: "%.8f, %.8f", result.coordinate.latitude, result.coordinate.longitude))
                            .textSelection(.enabled)
                        Text(result.placement + height(result)).font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Copy GPS") { probe.copyCoordinate() }
                    Button("Focus marker") { controller.focus(on: result.local, radius: max(5, result.accuracy * 2)) }
                }
            }
            HSplitView {
                VStack(alignment: .leading, spacing: 6) {
                    Toggle("Satellite", isOn: $satellite).font(.caption)
                    NativePhotoMap(
                        cameras: [], identity: artifact.presentation.sceneIdentity, satellite: satellite,
                        selected: $selectedCamera, modelOverlay: artifact.overlay,
                        testLocation: probe.mapLocation, testAccuracy: probe.mapAccuracy,
                        pickLocation: { probe.pickMap($0, in: artifact) })
                }.frame(minWidth: 240, idealWidth: 340)
                Artifact3DViewer(
                    presentation: artifact.presentation, openFeatures: { _ in }, controller: controller,
                    showGround: $showGround, selectSurfacePoint: { probe.pickModel($0, in: artifact) }
                )
                .frame(minWidth: 320)
            }
            Text(
                "Click the map or a model surface. Pink markers remain visible through the model; the ring shows horizontal accuracy. This checks coordinate placement without changing calibration."
            )
            .font(.caption).foregroundStyle(.secondary)
        }
        .onAppear { probe.updateOverlay(in: artifact) }
        .onDisappear {
            if let scene = artifact.presentation.scene { GeographicProbeOverlay().update(scene: scene, result: nil) }
        }
    }

    private func field(_ title: String, text: Binding<String>, width: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(title).font(.caption)
            TextField(title, text: text).labelsHidden().textFieldStyle(.roundedBorder)
                .onSubmit { probe.show(in: artifact) }
        }.frame(width: width)
    }

    private func height(_ result: GeographicProbeResult) -> String {
        guard let altitude = result.coordinate.altitude else { return " · absolute altitude unavailable" }
        return String(format: " · %.2f m · %@", altitude, result.coordinate.altitudeDatum ?? "unknown reference")
    }
}
