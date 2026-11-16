import SceneKit
import SwiftUI

struct ExclusionWorkspaceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    let presentation: ArtifactPresentation
    let openFeatures: (String) -> Void
    @ObservedObject var controller: SceneViewportController
    @Binding var showGround: Bool
    @State private var editing = false
    @State private var regions: [ExclusionRegion] = []
    @State private var selectedID: String?
    @State private var placing = false

    private var bounds: SceneBounds {
        let box = presentation.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.boundingBox
        return SceneBounds(
            positions: box.map { [[$0.min.x, $0.min.y, $0.min.z], [$0.max.x, $0.max.y, $0.max.z]] } ?? [])
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Button(editing ? "Close crop editor" : "Exclusion regions", systemImage: "crop") {
                    if !editing {
                        regions = workspace.inspection?.project.settings.exclusionRegions ?? []
                        selectedID = regions.first?.id
                    }
                    editing.toggle()
                    placing = false
                }
                .disabled(workspace.inspection == nil)
                if editing {
                    Text("Inside the red volumes will be excluded from new processing.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            HStack(alignment: .top, spacing: 12) {
                Artifact3DViewer(
                    presentation: presentation, openFeatures: openFeatures,
                    controller: controller, showGround: $showGround,
                    exclusionRegions: editing ? regions : [], selectedExclusionID: selectedID,
                    placeExclusion: placing ? place : nil)
                if editing {
                    Divider()
                    editor.frame(width: 280)
                }
            }
        }
        .onChange(of: workspace.displayedRun?.id) { _, _ in
            editing = false
            placing = false
        }
    }

    private var editor: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("Exclude regions").font(.headline)
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
                        Text("\(index + 1). \(region.shape.title)\(region.enabled ? "" : " (off)")")
                            .tag(Optional(region.id))
                    }
                }
            }
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
                    Text("Add a box or vertical cylinder, then place and resize it around unwanted points.")
                        .font(.callout).foregroundStyle(.secondary)
                }
            }
            Text("Save, then process again from Dense cloud. Existing runs keep their original regions.")
                .font(.caption).foregroundStyle(.secondary)
            Button(workspace.busy ? "Saving…" : "Save exclusions") {
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
}
