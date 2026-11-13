import SwiftUI

struct Artifact3DViewer: View {
    let presentation: ArtifactPresentation
    let openFeatures: (String) -> Void
    @StateObject private var controller = SceneViewportController()
    @AppStorage("viewer.showGroundGrid") private var showGround = false
    @State private var selectedTrack: Int?
    @State private var observationIndex = 0.0

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Button("Reset view", systemImage: "arrow.counterclockwise") { controller.reset() }
                    .help("Fit the entire cloud or mesh to the viewport")
                Toggle(isOn: $showGround) { Label("Ground grid", systemImage: "grid") }
                    .toggleStyle(.button)
                    .help("Show or hide the reference ground grid beneath the artifact")
                Text(presentation.metricScale ? "1 m cells · 10 m guides" : "1 unit cells · metric scale unavailable")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Text(
                    presentation.sparseInspection == nil
                        ? "Drag to orbit · scroll to zoom · Option-drag to pan"
                        : "Click a point to inspect · drag to orbit · scroll to zoom"
                )
                .font(.caption).foregroundStyle(.secondary)
            }.controlSize(.small)
            HStack(alignment: .top, spacing: 12) {
                SceneViewport(
                    presentation: presentation, controller: controller, showGround: showGround,
                    selectedTrack: selectedTrack,
                    selection: {
                        selectedTrack = $0
                        observationIndex = 0
                    })
                if let data = presentation.sparseInspection {
                    Divider()
                    VStack(alignment: .leading, spacing: 10) {
                        if let selectedTrack, data.tracks.indices.contains(selectedTrack) {
                            let track = data.tracks[selectedTrack]
                            Text("Point \(selectedTrack + 1) · \(track.observations.count) source photos").font(
                                .headline)
                            Text(
                                track.position.map { $0.formatted(.number.precision(.fractionLength(3))) }.joined(
                                    separator: ", ")
                            )
                            .font(.caption).textSelection(.enabled)
                            HStack {
                                Button("Previous photo", systemImage: "chevron.left") {
                                    observationIndex = max(0, observationIndex - 1)
                                }
                                .disabled(observationIndex == 0)
                                Text("Photo \(Int(observationIndex) + 1) of \(track.observations.count)").font(.caption)
                                Button("Next photo", systemImage: "chevron.right") {
                                    observationIndex = min(Double(track.observations.count - 1), observationIndex + 1)
                                }
                                .disabled(Int(observationIndex) >= track.observations.count - 1)
                            }.labelStyle(.iconOnly)
                            if track.observations.count > 1 {
                                Slider(value: $observationIndex, in: 0...Double(track.observations.count - 1), step: 1)
                                    .accessibilityLabel("Source photograph")
                            }
                            SparseObservationView(
                                data: data,
                                observation: track.observations[
                                    min(Int(observationIndex), track.observations.count - 1)],
                                openFeatures: openFeatures)
                        } else {
                            ContentUnavailableView(
                                "Select a sparse point", systemImage: "cursorarrow.rays",
                                description: Text(
                                    "Click a point in the cloud to see the photographs and feature patches that formed it. Yellow rays connect the point to its source cameras."
                                ))
                        }
                    }.frame(width: 340).frame(maxHeight: .infinity, alignment: .top)
                }
            }
        }
        .onChange(of: presentation.id) { _, _ in
            selectedTrack = nil
            observationIndex = 0
        }
    }
}
