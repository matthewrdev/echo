import SceneKit
import SwiftUI

struct ExclusionWorkspaceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var textureModel: TexturePriorityEditorModel
    let presentation: ArtifactPresentation
    let openFeatures: (String) -> Void
    @ObservedObject var controller: SceneViewportController
    @Binding var showGround: Bool
    @Binding var editing: Bool
    @Binding var editingTexture: Bool

    private var alignment: String { workspace.displayedRun?.artifacts["alignment"] ?? "" }
    @State private var regions: [ExclusionRegion] = []
    @State private var selectedID: String?
    @State private var placing = false

    private var bounds: SceneBounds {
        let box = presentation.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.boundingBox
        return SceneBounds(
            positions: box.map { [[$0.min.x, $0.min.y, $0.min.z], [$0.max.x, $0.max.y, $0.max.z]] } ?? [])
    }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Artifact3DViewer(
                presentation: presentation, openFeatures: openFeatures,
                controller: controller, showGround: $showGround,
                exclusionRegions: editing ? regions : [], selectedExclusionID: selectedID,
                placeExclusion: placing ? place : nil,
                changeExclusion: editing ? change : nil,
                texturePainting: editingTexture && !textureModel.incompatible ? textureModel.painting : nil,
                textureBrush: editingTexture && textureModel.paintingEnabled && !textureModel.incompatible
                    ? textureModel.brush : nil,
                showTexturePriority: editingTexture && textureModel.showHeatmap,
                finishTextureStroke: textureModel.append)
            if editing {
                Divider()
                editor.frame(width: 280)
            } else if editingTexture {
                Divider()
                TexturePriorityEditor(workspace: workspace, model: textureModel).frame(width: 280)
            }
        }
        .onChange(of: editing) { _, active in
            if active { loadCropRegions() }
            placing = false
        }
        .onChange(of: editingTexture) { _, active in
            if active {
                textureModel.followRun(workspace.inspection?.project.settings.texturePriority, alignment: alignment)
            }
            placing = false
        }
        .onChange(of: alignment) { _, _ in
            editingTexture = false
            textureModel.followRun(workspace.inspection?.project.settings.texturePriority, alignment: alignment)
        }
        .onChange(of: workspace.displayedRun?.id) { _, _ in
            editing = false
            editingTexture = false
            textureModel.followRun(workspace.inspection?.project.settings.texturePriority, alignment: alignment)
            placing = false
        }
        .onAppear {
            if editing { loadCropRegions() }
            textureModel.followRun(workspace.inspection?.project.settings.texturePriority, alignment: alignment)
        }
    }

    private func loadCropRegions() {
        regions = workspace.inspection?.project.settings.exclusionRegions ?? []
        selectedID = regions.first?.id
    }

    private var editor: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("Crop regions").font(.headline)
                Spacer()
                Menu("Add", systemImage: "plus") {
                    ForEach(ExclusionShape.allCases, id: \.self) { shape in
                        Button(shape.title) {
                            let region = ExclusionRegion(shape: shape, bounds: bounds)
                            regions.append(region)
                            selectedID = region.id
                        }
                    }
                }.fixedSize()
            }
            if !regions.isEmpty {
                Picker("Region", selection: $selectedID) {
                    ForEach(Array(regions.enumerated()), id: \.element.id) { index, region in
                        Text(
                            "\(index + 1). \(region.mode.title) \(region.shape.title)\(region.enabled ? "" : " (off)")"
                        )
                        .tag(Optional(region.id))
                    }
                }
            }
            CropPointCountView(scene: presentation.scene, regions: regions).id(presentation.id)
            ScrollView {
                if let index = regions.firstIndex(where: { $0.id == selectedID }) {
                    VStack(alignment: .leading, spacing: 12) {
                        ExclusionRegionEditor(
                            region: $regions[index], bounds: bounds, metricScale: presentation.metricScale)
                        Button(placing ? "Cancel placement" : "Place on surface", systemImage: "scope") {
                            placing.toggle()
                        }
                        if placing {
                            Text("Click a point or triangle to centre this region there.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        Button("Remove region", role: .destructive) {
                            regions.remove(at: index)
                            selectedID = regions.first?.id
                            placing = false
                        }
                    }
                } else {
                    Text(
                        "Add a box or cylinder, then choose Include or Exclude and adjust its position and size. Boxes can rotate around the vertical axis."
                    )
                    .font(.callout).foregroundStyle(.secondary)
                }
            }
            Text("Save, then process again from Fusion. The point cloud and texture priorities stay reusable.")
                .font(.caption).foregroundStyle(.secondary)
            Button(workspace.busy ? "Saving…" : "Save crop regions") {
                guard var settings = workspace.inspection?.project.settings else { return }
                settings.exclusionRegions = regions
                workspace.save(settings: settings)
            }
            .buttonStyle(.borderedProminent)
            .disabled(!workspace.canEdit || !regions.allSatisfy(\.isValid))
            if workspace.inspection?.project.settings.exclusionRegions == regions {
                Label("Saved", systemImage: "checkmark.circle").font(.caption).foregroundStyle(.green)
            }
        }
    }

    private func place(_ point: SCNVector3) {
        guard let index = regions.firstIndex(where: { $0.id == selectedID }) else { return }
        regions[index].place(at: point)
        placing = false
    }

    private func change(_ region: ExclusionRegion) {
        guard let index = regions.firstIndex(where: { $0.id == region.id }) else { return }
        regions[index] = region
    }
}
