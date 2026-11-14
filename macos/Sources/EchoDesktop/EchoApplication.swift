import SwiftUI

@main
struct EchoApplication: App {
    @NSApplicationDelegateAdaptor(EchoApplicationDelegate.self) private var appDelegate
    @StateObject private var workspace: WorkspaceViewModel
    @StateObject private var artifacts: ArtifactViewModel
    @StateObject private var runtimeInfo: RuntimeInfoViewModel
    @State private var showsRuntimeInfo = false

    init() {
        let options = LaunchOptions(arguments: CommandLine.arguments, bundle: .main)
        let cli = ProcessCLIClient(executable: options.cli)
        let projects = CLIProjectClient(cli: cli)
        _runtimeInfo = StateObject(
            wrappedValue: RuntimeInfoViewModel(
                cli: cli,
                native: NativeRuntimeInfo(bundle: .main, processorURL: options.cli), clipboard: SystemTextClipboard()))
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer()))
        let formatter = RunEventFormatter()
        let workspace = WorkspaceViewModel(
            client: projects, project: options.project, logReader: RunLogReader(formatter: formatter),
            eventFormatter: formatter, recentProjectStore: UserDefaultsRecentProjectStore(defaults: .standard),
            clipboard: SystemTextClipboard())
        _workspace = StateObject(wrappedValue: workspace)
        _artifacts = StateObject(wrappedValue: ArtifactViewModel(reader: reader))
        appDelegate.termination = ApplicationTerminationCoordinator(
            work: workspace, confirmation: NativeQuitConfirmation())
    }

    var body: some Scene {
        Window("Echo · Photogrammetry", id: "echo") {
            WorkspaceView(workspace: workspace, artifacts: artifacts, showRuntimeInfo: { showsRuntimeInfo = true })
                .disclosureGroupStyle(ClickableDisclosureStyle())
                .sheet(isPresented: $showsRuntimeInfo) { RuntimeInfoView(model: runtimeInfo) }
        }
        .defaultSize(width: 1500, height: 960)
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("About Echo…") { showsRuntimeInfo = true }
            }
            CommandGroup(replacing: .newItem) {
                Button("New Project…") { workspace.createProject() }.keyboardShortcut("n")
                Button("Open Project…") { workspace.openProject() }.keyboardShortcut("o")
            }
        }
    }
}
