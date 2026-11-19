import SceneKit
import SwiftUI

struct Artifact3DViewer: View {
    let presentation: ArtifactPresentation
    let openFeatures: (String) -> Void
    @ObservedObject var controller: SceneViewportController
    @Binding var showGround: Bool
    var exclusionRegions: [ExclusionRegion] = []
    var selectedExclusionID: String?
    var selectSurfacePoint: ((SCNVector3) -> Void)?
    var cropBrush: CropBrushConfiguration?
    var finishCropStroke: ((ExclusionRegion) -> Void)?
    var cropStrokeError: ((String) -> Void)?
    var placeExclusion: ((SCNVector3) -> Void)?
    var changeExclusion: ((ExclusionRegion) -> Void)?
    var texturePainting: TexturePriorityPainting?
    var textureBrush: TextureBrushConfiguration?
    var showTexturePriority = false
    var finishTextureStroke: ((TexturePriorityStroke) -> Void)?
    @State private var selected: SparseSelection?
    @State private var observationIndex = 0.0

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .top, spacing: 12) {
                SceneViewport(
                    presentation: presentation, controller: controller, showGround: showGround,
                    selected: selected,
                    selection: {
                        selected = $0
                        observationIndex = 0
                    }, exclusionRegions: exclusionRegions, selectedExclusionID: selectedExclusionID,
                    selectSurfacePoint: selectSurfacePoint, cropBrush: cropBrush, finishCropStroke: finishCropStroke,
                    cropStrokeError: cropStrokeError, placeExclusion: placeExclusion,
                    changeExclusion: changeExclusion,
                    texturePainting: texturePainting, textureBrush: textureBrush,
                    showTexturePriority: showTexturePriority, finishTextureStroke: finishTextureStroke
                )
                .overlay(alignment: .bottomLeading) {
                    if let data = presentation.cameraInspection, let scene = presentation.scene,
                        case .camera(let imageID) = selected
                    {
                        SourceCameraComparison(
                            data: data, imageID: imageID, scene: scene, move: controller.moveCamera,
                            cameraKey: controller.handleCameraKey
                        ) { selected = nil }
                        .frame(maxWidth: 520).padding(10).padding(.bottom, 42)
                    }
                }
                .overlay(alignment: .topTrailing) {
                    VStack(spacing: 0) {
                        SceneOrientationView(orientation: controller.orientation, metricScale: presentation.metricScale)
                        SceneOrientationControls(controller: controller, metricScale: presentation.metricScale)
                            .padding(.bottom, 5)
                    }
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8)).padding(10)
                }
                .overlay(alignment: .bottomTrailing) {
                    VStack(alignment: .trailing, spacing: 10) {
                        SceneViewControls(
                            controller: controller, showGround: $showGround, metricScale: presentation.metricScale
                        )
                        .padding(presentation.fusion == nil ? 0 : 6)
                        .background {
                            if presentation.fusion != nil {
                                RoundedRectangle(cornerRadius: 6).fill(.regularMaterial)
                            }
                        }
                        Text(presentation.detail).font(.caption).foregroundStyle(.secondary)
                            .multilineTextAlignment(.trailing).fixedSize(horizontal: false, vertical: true)
                            .padding(6).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 6))
                    }.padding(10)
                }
                if let data = presentation.sparseInspection, let selected {
                    Divider()
                    VStack(alignment: .leading, spacing: 10) {
                        HStack {
                            Spacer()
                            Button("Clear selection", systemImage: "xmark") { self.selected = nil }
                                .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).help("Clear selection")
                        }
                        if case .point(let selectedTrack) = selected, data.tracks.indices.contains(selectedTrack) {
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
                            }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain)
                            if track.observations.count > 1 {
                                Slider(value: $observationIndex, in: 0...Double(track.observations.count - 1), step: 1)
                                    .accessibilityLabel("Source photograph")
                            }
                            SparseObservationView(
                                data: data,
                                observation: track.observations[
                                    min(Int(observationIndex), track.observations.count - 1)],
                                openFeatures: openFeatures)
                        } else if case .camera(let imageID) = selected {
                            SparseCameraInspector(data: data, imageID: imageID, openFeatures: openFeatures)
                        }
                    }.frame(width: 340).frame(maxHeight: .infinity, alignment: .top)
                }
            }
        }
        .onChange(of: texturePainting != nil) { _, active in
            if active { selected = nil }
        }
        .onChange(of: presentation.id) { _, _ in
            if presentation.cameraInspection != nil { return }
            selected = nil
            observationIndex = 0
        }
        .onChange(of: presentation.sceneIdentity) { _, _ in
            selected = nil
            observationIndex = 0
        }
    }
}
