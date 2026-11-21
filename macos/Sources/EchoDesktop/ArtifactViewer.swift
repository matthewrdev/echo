import EchoRendering
import SwiftUI

struct ArtifactViewer: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var model: ArtifactViewModel
    @StateObject private var sceneController = SceneViewportController()
    @State private var openingSelection = true
    @State private var editingCrop = false
    @State private var editingTexture = false
    @State private var editingColour = false
    @AppStorage("viewer.showGroundGrid") private var showGround = false

    private var artifactKey: String {
        let stage = workspace.selectedStage.rawValue
        let matching = stage == "features" ? workspace.displayedRun?.artifacts["matching"] ?? "" : ""
        var previewVersion = ""
        if workspace.selectedStage == .matching, let run = workspace.displayedRun,
            run.artifacts[stage] == nil, let path = run.artifacts[stage + "Preview"]
        {
            let file = URL(fileURLWithPath: workspace.root).appendingPathComponent(path).appendingPathComponent(
                "pairs.ndjson")
            let version = ArtifactFileVersion(file: file)
            previewVersion = "\(version.modified?.timeIntervalSince1970 ?? 0)/\(version.size ?? 0)/\(run.status)"
        }
        if [.alignment, .fusion].contains(workspace.selectedStage), let run = workspace.displayedRun,
            run.artifacts[stage] == nil, let path = run.artifacts[stage + "Preview"]
        {
            // Preview slots are reused. Include their version even if polling missed a slot change.
            let folder = URL(fileURLWithPath: workspace.root).appendingPathComponent(path)
            let binary = folder.appendingPathComponent("preview.bin")
            let file =
                workspace.selectedStage == .alignment && FileManager.default.fileExists(atPath: binary.path)
                ? binary : folder.appendingPathComponent("preview.json")
            let version = ArtifactFileVersion(file: file)
            previewVersion = "\(version.modified?.timeIntervalSince1970 ?? 0)/\(version.size ?? 0)/\(run.status)"
        }
        return matching + workspace.root + (workspace.displayedRun?.id ?? "") + stage
            + (workspace.displayedRun?.artifacts[stage] ?? workspace.displayedRun?.artifacts[stage + "Preview"]
                ?? "") + previewVersion
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if showsToolbar {
                HStack {
                    if workspace.selectedStage == .cloud {
                        CloudDisplayControls(
                            model: model, hasSavedCloud: workspace.displayedRun?.artifacts["cloud"] != nil)
                    } else if workspace.selectedStage == .alignment {
                        SparseDisplayControls(model: model)
                    } else if showsViewPicker {
                        Picker(
                            "View",
                            selection: Binding(get: { model.mode }, set: { model.selectMode($0) })
                        ) {
                            ForEach(model.modes, id: \.self) { Text($0) }
                        }.pickerStyle(.segmented).fixedSize()
                            .help("Saved artifact view")
                    }
                    Spacer(minLength: 12)
                    if workspace.selectedStage == .cloud, model.presentation?.scene != nil {
                        editingTools
                    }
                }
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
            if workspace.selectedStage == .alignment,
                workspace.displayedRun?.status == "running",
                workspace.displayedRun?.artifacts["alignment"] == nil
            {
                Label {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(
                            "Alignment in progress. Camera and point positions are provisional and may appear incorrect until alignment finishes."
                        )
                        if model.presentation?.metricScale == false {
                            Text("This preview is not yet oriented to geographic up and may appear upside down.")
                        }
                    }
                } icon: {
                    Image(systemName: "info.circle")
                }
                .font(.callout).fixedSize(horizontal: false, vertical: true)
                .padding(10).frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.accentColor.opacity(0.08), in: RoundedRectangle(cornerRadius: 8))
                .accessibilityIdentifier("alignment.provisionalNotice")
            }
            if workspace.stageStatus(workspace.selectedStage) == "Out of date" {
                Label(
                    "These results use earlier inputs. Process again to rebuild this stage and its dependencies.",
                    systemImage: "exclamationmark.triangle.fill"
                )
                .font(.callout).foregroundStyle(.orange)
            }
            if [.features, .matching].contains(workspace.selectedStage), let imageReadiness {
                Text(imageReadiness).font(.caption).foregroundStyle(.secondary)
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
                        workspace.selectedStage == .cloud ? "Loading point preview…" : "Opening artifact…")
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
        .onChange(of: workspace.selectedStage) { _, _ in
            editingColour = false
            editingCrop = false
            editingTexture = false
        }
        .onChange(of: workspace.displayedRun?.id) { _, _ in
            workspace.colourGrading.selection = nil
            editingColour = false
            editingCrop = false
            editingTexture = false
        }
        .onChange(of: workspace.root) { _, _ in
            editingColour = false
            editingCrop = false
            editingTexture = false
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

    private var showsToolbar: Bool {
        showsViewPicker || [.cloud, .alignment].contains(workspace.selectedStage)
    }

    private var showsViewPicker: Bool {
        !model.modes.isEmpty && workspace.selectedStage != .depth
            && (workspace.selectedStage != .fusion || workspace.displayedRun?.artifacts["fusion"] != nil)
    }

    private var supportsPainting: Bool {
        let geometry = model.presentation?.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.geometry
        return workspace.displayedRun?.artifacts["alignment"] != nil
            && geometry?.elements.contains { $0.primitiveType == .point } == true
    }

    private var editingTools: some View {
        HStack(spacing: 8) {
            Button("Colour grading", systemImage: "slider.horizontal.3") {
                editingColour.toggle()
                editingCrop = false
                editingTexture = false
            }
            .labelStyle(ViewerIconLabelStyle(selected: editingColour))
            .help("Colour grading and histograms")
            .accessibilityIdentifier("colourGrade.open")
            Button(editingCrop ? "Close crop editor" : "Crop regions", systemImage: "crop") {
                editingColour = false
                editingCrop.toggle()
                editingTexture = false
            }
            .labelStyle(ViewerIconLabelStyle(selected: editingCrop))
            .help(editingCrop ? "Close crop editor" : "Crop regions")
            if supportsPainting {
                Button(
                    editingTexture ? "Close texture brush" : "Texture priority", systemImage: "paintbrush.pointed"
                ) {
                    editingColour = false
                    editingTexture.toggle()
                    editingCrop = false
                }
                .labelStyle(ViewerIconLabelStyle(selected: editingTexture))
                .help(editingTexture ? "Close texture brush" : "Texture priority")
            }
        }
        .buttonStyle(.plain)
        .disabled(workspace.inspection == nil)
    }

    private var imageReadiness: String? {
        guard [.features, .matching, .depth].contains(workspace.selectedStage), let run = workspace.displayedRun else {
            return nil
        }
        let stage = workspace.selectedStage.rawValue
        let partial = run.artifacts[stage] == nil && run.artifacts[stage + "Preview"] != nil
        guard partial || workspace.selectedStage == .depth else { return nil }
        if workspace.selectedStage == .matching {
            if let progress = run.progress, progress.stage == stage, let total = progress.total {
                return "\(model.units.count) / \(total) image pairs ready"
            }
            return "\(model.units.count) image pairs ready"
        }
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
                    GradedCloudWorkspace(
                        workspace: workspace, textureModel: workspace.texturePriorityEditor, presentation: value,
                        openFeatures: { photoID in
                            workspace.selectedPhotoID = photoID
                            workspace.selectedStage = .features
                        }, controller: sceneController, showGround: $showGround,
                        editing: $editingCrop, editingTexture: $editingTexture,
                        grading: workspace.colourGrading, editingColour: editingColour,
                        minimumSupport: model.depthAgreementPercentage / 100)
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
                    hasSavedArtifact && model.error != nil
                        ? "Couldn’t open saved output"
                        : workspace.isWorking ? "Waiting for this stage’s output" : "No saved output yet",
                    systemImage: workspace.selectedStage == .cloud
                        ? "point.3.connected.trianglepath.dotted" : "square.stack.3d.up",
                    description: Text(
                        workspace.selectedStage == .cloud && workspace.isWorking && !hasSavedArtifact
                            ? "A sampled point cloud will appear here as supported points are accepted."
                            : model.error ?? "Process to this stage to create its artifact.")
                )
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }

    private var hasSavedArtifact: Bool {
        let artifacts = workspace.displayedRun?.artifacts ?? [:]
        let stage = workspace.selectedStage.rawValue
        return artifacts[stage] != nil || artifacts[stage + "Preview"] != nil
    }
}
