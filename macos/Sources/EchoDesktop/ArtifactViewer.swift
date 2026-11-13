import SwiftUI

struct ArtifactViewer: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var model: ArtifactViewModel
    @StateObject private var sceneController = SceneViewportController()
    @AppStorage("viewer.showGroundGrid") private var showGround = false

    private var artifactKey: String {
        let stage = workspace.selectedStage.rawValue
        let matching = stage == "features" ? workspace.inspection?.run?.artifacts["matching"] ?? "" : ""
        return matching + workspace.root + (workspace.inspection?.run?.id ?? "") + stage
            + (workspace.inspection?.run?.artifacts[stage] ?? workspace.inspection?.run?.artifacts[stage + "Preview"]
                ?? "")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top) {
                Text(workspace.selectedStage.title).font(.title2.bold())
                Spacer(minLength: 16)
                HStack(spacing: 8) {
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
                                !workspace.canEdit || workspace.inspection?.run == nil
                                    || workspace.inspection?.run?.status == "completed")
                        Button("Reset from this stage…") { workspace.reset(from: workspace.selectedStage) }
                            .disabled(!workspace.canEdit)
                        Button("Manage generated files…") { workspace.sheet = .storage }
                        Divider()
                        Button("Show artifact files") { workspace.revealArtifact(workspace.selectedStage) }
                            .disabled(workspace.inspection?.run?.artifacts[workspace.selectedStage.rawValue] == nil)
                    } label: {
                        Label("More stage actions", systemImage: "ellipsis")
                    }
                    .menuStyle(.borderlessButton).menuIndicator(.hidden).help("More stage actions")
                    .labelStyle(.iconOnly).modifier(ViewerIconFrame()).accessibilityLabel("More stage actions")
                }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).fixedSize()
            }
            if !model.modes.isEmpty {
                Picker(
                    workspace.selectedStage == .cloud ? "Depth agreement" : "View",
                    selection: Binding(get: { model.mode }, set: { model.selectMode($0) })
                ) {
                    ForEach(model.modes, id: \.self) { Text($0) }
                }.pickerStyle(.segmented).fixedSize()
                    .help(
                        workspace.selectedStage == .cloud
                            ? "Fraction of tested neighbouring depth maps that agree with each point. Filters the display only; not a probability of correctness."
                            : "Saved artifact view")
            }
            if workspace.stageStatus(workspace.selectedStage) == "Out of date" {
                Label(
                    "These results use earlier inputs. Process again to rebuild this stage and its dependencies.",
                    systemImage: "exclamationmark.triangle.fill"
                )
                .font(.callout).foregroundStyle(.orange)
            }
            if [.features, .depth].contains(workspace.selectedStage), let run = workspace.inspection?.run,
                run.artifacts[workspace.selectedStage.rawValue] == nil,
                run.artifacts[workspace.selectedStage.rawValue + "Preview"] != nil
            {
                let total =
                    run.progress?.stage == workspace.selectedStage.rawValue
                    ? run.progress?.total ?? run.snapshot.images.filter(\.enabled).count
                    : run.snapshot.images.filter(\.enabled).count
                Text("\(model.units.count) / \(total) images ready")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if workspace.selectedStage == .cloud, workspace.inspection?.run?.artifacts["cloud"] != nil {
                HStack {
                    Text("Point detail")
                    Slider(
                        value: $model.pointPercentage, in: 1...100, step: 1,
                        onEditingChanged: { editing in
                            if !editing { Task { await model.load() } }
                        }
                    ).frame(maxWidth: 260).accessibilityLabel("Percentage of saved points")
                    Text("\(Int(model.pointPercentage))%")
                        .monospacedDigit().frame(width: 40, alignment: .trailing)

                }.font(.caption)
            }
            if workspace.selectedStage == .fusion {
                HStack {
                    Text("Volume slice")
                    Slider(
                        value: $model.slice, in: 0...1,
                        onEditingChanged: { editing in
                            if !editing { Task { await model.load() } }
                        })
                }
            }
            HStack(alignment: .top, spacing: 12) {
                if !model.units.isEmpty {
                    ArtifactUnitBrowser(units: model.units, selected: model.selectedUnit, selection: model.selectUnit)
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
                root: workspace.root, run: workspace.inspection?.run, stage: workspace.selectedStage,
                preferredPhotoID: workspace.selectedPhotoID)
        }
    }

    private var artifactContent: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let value = model.presentation {
                if value.scene == nil { Text(value.detail).font(.caption).foregroundStyle(.secondary) }
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
                    Text(value.title).font(.caption.weight(.semibold))
                    DepthImageViewport(data: depth)
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
