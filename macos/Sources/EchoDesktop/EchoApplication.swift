import SwiftUI

@main
struct EchoApplication: App {
    @NSApplicationDelegateAdaptor(EchoApplicationDelegate.self) private var appDelegate
    @StateObject private var workspace: WorkspaceViewModel
    @StateObject private var artifacts: ArtifactViewModel

    init() {
        let options = LaunchOptions(arguments: CommandLine.arguments, bundle: .main)
        let cli = ProcessCLIClient(executable: options.cli)
        let projects = CLIProjectClient(cli: cli)
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer())
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
            WorkspaceView(workspace: workspace, artifacts: artifacts)
        }
        .defaultSize(width: 1500, height: 960)
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("New Project…") { workspace.createProject() }.keyboardShortcut("n")
                Button("Open Project…") { workspace.openProject() }.keyboardShortcut("o")
            }
        }
    }
}
