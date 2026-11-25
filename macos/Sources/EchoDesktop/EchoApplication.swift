import EchoDrone
import EchoRendering
import SwiftUI

@main
struct EchoApplication: App {
    @NSApplicationDelegateAdaptor(EchoApplicationDelegate.self) private var appDelegate
    @StateObject private var workspace: WorkspaceViewModel
    @StateObject private var artifacts: ArtifactViewModel
    @StateObject private var runtimeInfo: RuntimeInfoViewModel
    @StateObject private var droneImport: DroneImportViewModel
    @State private var showsRuntimeInfo = false
    @State private var showsAttribution = false

    init() {
        let options = LaunchOptions(arguments: CommandLine.arguments, bundle: .main)
        let commandLog = CLICommandLog()
        let cli = ProcessCLIClient(executable: options.cli, commandLog: commandLog)
        let projects = CLIProjectClient(cli: cli, traces: PackedTraceReader())
        let droneImport = DroneImportViewModel(
            service: DroneImportService(cli: cli, reader: DroneSourceReader(), grouping: DroneImportGrouping()))
        _droneImport = StateObject(wrappedValue: droneImport)
        _runtimeInfo = StateObject(
            wrappedValue: RuntimeInfoViewModel(
                cli: cli,
                native: NativeRuntimeInfo(bundle: .main, processorURL: options.cli), clipboard: SystemTextClipboard()))
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let formatter = RunEventFormatter()
        let workspace = WorkspaceViewModel(
            client: projects, project: options.project, logReader: RunLogReader(formatter: formatter),
            eventFormatter: formatter, recentProjectStore: UserDefaultsRecentProjectStore(defaults: .standard),
            clipboard: SystemTextClipboard(), commandLog: commandLog,
            projectThumbnails: ProjectThumbnailLoader(scenes: NativeSceneFactory(), json: json, glb: GlbAssetReader()),
            fileDialogLocations: FileDialogLocations(defaults: .standard))
        _workspace = StateObject(wrappedValue: workspace)
        _artifacts = StateObject(wrappedValue: ArtifactViewModel(reader: reader))
        appDelegate.isImportingDrone = { [weak droneImport] in droneImport?.busy == true }
        appDelegate.termination = ApplicationTerminationCoordinator(
            work: workspace, confirmation: NativeQuitConfirmation())
    }

    var body: some Scene {
        Window("Projects", id: "echo") {
            WorkspaceView(
                workspace: workspace, artifacts: artifacts, droneImport: droneImport,
                showRuntimeInfo: { showsRuntimeInfo = true }
            )
            .disclosureGroupStyle(ClickableDisclosureStyle())
            .sheet(isPresented: $showsRuntimeInfo) { RuntimeInfoView(model: runtimeInfo) }
            .sheet(isPresented: $showsAttribution) { AttributionView() }
        }
        .defaultSize(width: 1500, height: 960)
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("About Echo…") { showsRuntimeInfo = true }
                Button("Dependencies & Attribution…") { showsAttribution = true }
            }
            CommandGroup(replacing: .newItem) {
                Button("New Project…") { workspace.createProject() }.keyboardShortcut("n")
                Button("Open Project…") { workspace.openProject() }.keyboardShortcut("o")
            }
        }
    }
}
