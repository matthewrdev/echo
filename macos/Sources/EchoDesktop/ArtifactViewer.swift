import SwiftUI

struct ArtifactViewer: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var model: ArtifactViewModel
    @StateObject private var sceneController = SceneViewportController()
    @State private var openingSelection = true
    @AppStorage("viewer.showGroundGrid") private var showGround = false

    private var artifactKey: String {
        let stage = workspace.selectedStage.rawValue
        let matching = stage == "features" ? workspace.displayedRun?.artifacts["matching"] ?? "" : ""
        var previewVersion = ""
        if [.alignment, .fusion].contains(workspace.selectedStage), let run = workspace.displayedRun,
            run.artifacts[stage] == nil, let path = run.artifacts[stage + "Preview"]
        {
            // Preview slots are reused. Include their version even if polling missed a slot change.
            let file = URL(fileURLWithPath: workspace.root).appendingPathComponent(path)
                .appendingPathComponent("preview.json")
            let version = ArtifactFileVersion(file: file)
            previewVersion = "\(version.modified?.timeIntervalSince1970 ?? 0)/\(version.size ?? 0)/\(run.status)"
        }
        return matching + workspace.root + (workspace.displayedRun?.id ?? "") + stage
            + (workspace.displayedRun?.artifacts[stage] ?? workspace.displayedRun?.artifacts[stage + "Preview"]
                ?? "") + previewVersion
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top) {
                Text(workspace.selectedStage.title).font(.title2.bold())
                Spacer(minLength: 16)
                HStack(spacing: 8) {
                    if [.alignment, .cloud, .mesh, .texture, .export].contains(workspace.selectedStage) {
                        Button("View on map", systemImage: "map") { workspace.sheet = .georeference }
                            .disabled(workspace.displayedRun?.artifacts["alignment"] == nil)
                            .help("View geographic placement and export for QGIS")
                    }
                    Button("Process to \(workspace.selectedStage.shortTitle)", systemImage: "play.fill") {
                        workspace.start(through: workspace.selectedStage)
                    }
                    .disabled(
                        !workspace.canEdit || (workspace.inspection?.project.images.filter(\.enabled).count ?? 0) < 2
                    )
                    .help(
                        "Process to \(workspace.selectedStage.shortTitle). Reuse compatible saved work."
                    )
                    Button("Stage settings", systemImage: "gearshape") { workspace.sheet = .settings }
                        .help("\(workspace.selectedStage.shortTitle) settings")
                    Menu {
                        Button("Process all stages to GLB") { workspace.start(through: .export) }.disabled(
                            !workspace.canEdit)
                        Button("Resume this saved run") { workspace.resume() }
                            .disabled(
                                !workspace.canEdit || workspace.displayedRun == nil
                                    || workspace.displayedRun?.status == "completed")
                        Button("Reset from this stage…") { workspace.reset(from: workspace.selectedStage) }
                            .disabled(!workspace.canEdit)
                        Button("Manage generated files…") { workspace.sheet = .storage }
                        Divider()
                        Button("Show artifact files") { workspace.revealArtifact(workspace.selectedStage) }
                            .disabled(workspace.displayedRun?.artifacts[workspace.selectedStage.rawValue] == nil)
                    } label: {
                        Label("More stage actions", systemImage: "ellipsis")
                    }
                    .menuStyle(.borderlessButton).menuIndicator(.hidden).help("More stage actions")
                    .labelStyle(.iconOnly).modifier(ViewerIconFrame()).accessibilityLabel("More stage actions")
                }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).fixedSize()
            }
            if !model.modes.isEmpty, workspace.selectedStage != .depth,
                workspace.selectedStage != .fusion || workspace.displayedRun?.artifacts["fusion"] != nil
            {
                Picker(
                    "View",
                    selection: Binding(get: { model.mode }, set: { model.selectMode($0) })
                ) {
                    ForEach(model.modes, id: \.self) { Text($0) }
                }.pickerStyle(.segmented).fixedSize()
                    .help("Saved artifact view")
            }
            if [.mesh, .export].contains(workspace.selectedStage), model.mode == MeshViewMode.normals.rawValue {
                Text("Face normals · RGB = X, Y, Z (−1…1)")
                    .font(.caption).foregroundStyle(.secondary)
                    .help(
                        "Colors show each triangle’s normal from its winding, in model coordinates. They stay fixed as you orbit."
                    )
            }
            if workspace.retainedStages.contains(workspace.selectedStage) {
                Label(workspace.savedResultsNotice, systemImage: "checkmark.circle")
                    .font(.callout).foregroundStyle(.secondary)
            }
            if workspace.stageStatus(workspace.selectedStage) == "Out of date" {
                Label(
                    "These results use earlier inputs. Process again to rebuild this stage and its dependencies.",
                    systemImage: "exclamationmark.triangle.fill"
                )
                .font(.callout).foregroundStyle(.orange)
            }
            if workspace.selectedStage == .features, let imageReadiness {
                Text(imageReadiness).font(.caption).foregroundStyle(.secondary)
            }
            if workspace.selectedStage == .cloud {
                CloudDisplayControls(
                    model: model, hasSavedCloud: workspace.displayedRun?.artifacts["cloud"] != nil)
            }
            if workspace.selectedStage == .fusion, let info = model.presentation?.fusion {
                FusionLegendView(info: info)
            }
            if workspace.selectedStage == .fusion, workspace.displayedRun?.artifacts["fusion"] != nil,
                FusionViewMode(selection: model.mode).axis != nil
            {
                HStack {
                    Text("Slice position")
                    Slider(
                        value: $model.slice, in: 0...1,
                        onEditingChanged: { editing in
                            if !editing { Task { await model.load() } }
                        }
                    ).accessibilityLabel("Fusion slice position")
                }
            }
            HStack(alignment: .top, spacing: 12) {
                if !model.units.isEmpty {
                    ArtifactUnitBrowser(
                        units: model.units, selected: model.selectedUnit, selection: model.selectUnit,
                        footer: workspace.selectedStage == .depth ? imageReadiness : nil)
                    Divider()
                }
                artifactContent.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .overlay(alignment: .bottomTrailing) {
            if model.loading {
                HStack {
                    ProgressView(
                        model.pointPercentage == 100 && workspace.selectedStage == .cloud
                            ? "Loading all saved points…" : "Opening artifact…")
                    Button("Cancel") { model.cancelLoading() }
                }
                .controlSize(.small).padding(12).background(
                    .regularMaterial, in: RoundedRectangle(cornerRadius: 10)
                ).padding(20)
            }
        }
        .onChange(of: workspace.selectedPhotoID) { _, id in
            if workspace.selectedStage == .features || workspace.selectedStage == .depth,
                let id, let unit = model.units.first(where: { $0.imageIDs.contains(id) })
            {
                model.selectUnit(unit.id)
            }
        }
        .task(id: artifactKey) {
            await model.configure(
                root: workspace.root, run: workspace.displayedRun, stage: workspace.selectedStage,
                preferredPhotoID: workspace.selectedPhotoID)
            guard !Task.isCancelled else { return }
            if openingSelection, [.features, .depth].contains(workspace.selectedStage),
                let photoID = workspace.selectedPhotoID,
                let unit = model.units.first(where: { $0.imageIDs.contains(photoID) }), unit.id != model.selectedUnit
            {
                model.selectUnit(unit.id)
            }
            openingSelection = false
        }
    }

    private var imageReadiness: String? {
        guard [.features, .depth].contains(workspace.selectedStage), let run = workspace.displayedRun else {
            return nil
        }
        let stage = workspace.selectedStage.rawValue
        let partial = run.artifacts[stage] == nil && run.artifacts[stage + "Preview"] != nil
        guard partial || workspace.selectedStage == .depth else { return nil }
        let total =
            run.progress?.stage == stage
            ? run.progress?.total ?? run.snapshot.images.filter(\.enabled).count
            : run.snapshot.images.filter(\.enabled).count
        return "\(model.units.count) / \(total) images ready"
    }

    private var artifactContent: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let value = model.presentation {
                if value.scene == nil, value.depthImage == nil {
                    Text(value.detail).font(.caption).foregroundStyle(.secondary)
                }
                if let features = value.features {
                    FeatureViewer(data: features) { link in
                        model.requestMatch(link)
                        workspace.selectedStage = .matching
                    }
                } else if let matches = value.matches {
                    MatchingViewer(data: matches, selected: $model.selectedMatchIndex)
                } else if value.scene != nil {
                    Artifact3DViewer(
                        presentation: value,
                        openFeatures: { photoID in
                            workspace.selectedPhotoID = photoID
                            workspace.selectedStage = .features
                        }, controller: sceneController, showGround: $showGround)
                } else if let depth = value.depthImage {
                    HStack(spacing: 12) {
                        Text(value.title).fontWeight(.semibold).lineLimit(1).truncationMode(.middle)
                        Spacer(minLength: 0)
                        DepthQualityBadge(summary: depth.quality).fixedSize()
                        Text(value.detail).foregroundStyle(.secondary).lineLimit(1).fixedSize()
                    }.font(.caption)
                    DepthImageViewport(data: depth, mode: Binding(get: { model.mode }, set: { model.selectMode($0) }))
                } else if let image = value.image {
                    ImageViewport(image: image)
                }
            } else {
                ContentUnavailableView(
                    workspace.isWorking ? "Waiting for this stage’s output" : "No saved output yet",
                    systemImage: workspace.selectedStage == .cloud
                        ? "point.3.connected.trianglepath.dotted" : "square.stack.3d.up",
                    description: Text(
                        workspace.selectedStage == .cloud && workspace.isWorking
                            ? "A sampled point cloud will appear here as supported points are accepted."
                            : model.error ?? "Process to this stage to create its artifact.")
                )
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }
}
