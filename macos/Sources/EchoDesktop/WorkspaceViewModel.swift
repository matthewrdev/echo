import AppKit
import SwiftUI

@MainActor
final class WorkspaceViewModel: ObservableObject, ApplicationWork {
    private let client: ProjectClient
    private let clipboard: TextClipboard
    private let recentProjectStore: RecentProjectStore
    private var recordedRoot = ""
    private let logReader: RunLogReader
    private let eventFormatter: RunEventFormatter
    private let commandLog: CLICommandLog?
    private var refreshing = false
    private var logIDs: Set<Int> = []
    private var savedRunKey = ""
    private var savedRuns: [RunData] = []
    private var savedArtifactSources: [PipelineStage: RunData] = [:]
    @Published private(set) var displayedRun: RunData?
    @Published private(set) var retainedStages: Set<PipelineStage> = []
    let projectThumbnails: ProjectThumbnailLoader?
    let telemetry = TelemetryHistory()
    let performance: PerformanceTraceViewModel

    @Published var recentProjects: [RecentProject]
    @Published var openingProject = false
    @Published var showsTelemetry = false
    @Published var openingError: String?
    @Published var root: String
    @Published var inspection: Inspection?
    @Published var storageInventory: ArtifactStorageInventory?
    @Published var runs: [RunSummary] = []
    @Published var selectedRun = "latest"
    @Published var selectedPhotoID: String?
    @Published var query = ""
    @Published var selectedSet = "All sets"
    @Published var locationFilter = "All images"
    @Published var destination = WorkspaceDestination.images
    @Published var imagesViewMode = ImagesViewMode.tiles
    private var lastArtifactStage = PipelineStage.alignment
    @Published var sheet: WorkspaceSheet?
    @Published private(set) var logs: [RunLogEntry] = []
    @Published var commands: [CLICommandInvocation] = []
    @Published var commandLogError: String?
    @Published var workingRun: RunData?
    @Published var cancelRequested = false
    @Published var cancellationRequestedAt: Date?
    @Published var busy = false
    @Published private(set) var quitting = false
    @Published var status = "Open or create a project to begin."
    @Published var errorMessage: String?
    @Published var pendingChange: ProjectEditRequest?

    init(
        client: ProjectClient, project: String, logReader: RunLogReader, eventFormatter: RunEventFormatter,
        recentProjectStore: RecentProjectStore, clipboard: TextClipboard, commandLog: CLICommandLog? = nil,
        projectThumbnails: ProjectThumbnailLoader? = nil
    ) {
        self.clipboard = clipboard
        self.recentProjectStore = recentProjectStore
        recentProjects = recentProjectStore.load()
        self.logReader = logReader
        self.eventFormatter = eventFormatter
        self.client = client
        performance = PerformanceTraceViewModel(client: client)
        self.commandLog = commandLog
        self.projectThumbnails = projectThumbnails
        root = project
    }

    var selectedPhoto: Photo? {
        inspection?.project.images.first { $0.id == selectedPhotoID }
    }

    var selectedStage: PipelineStage {
        get {
            if case .artifact(let stage) = destination { return stage }
            return lastArtifactStage
        }
        set {
            lastArtifactStage = newValue
            destination = .artifact(newValue)
        }
    }

    var activeRun: RunSummary? { runs.first { $0.status == "running" } }
    var canEdit: Bool { !quitting && !busy && activeRun == nil }
    var sets: [String] { ["All sets"] + Set(inspection?.project.images.flatMap(\.sets) ?? []).sorted() }

    var photoGroups: [PhotoGroup] {
        var groups: [String: [Photo]] = [:]
        for photo in filteredPhotos {
            for name in photo.sets.isEmpty ? ["Ungrouped"] : photo.sets {
                groups[name, default: []].append(photo)
            }
        }
        return groups.keys.sorted().map { PhotoGroup(name: $0, photos: groups[$0]!) }
    }

    var isWorking: Bool { busy || activeRun != nil }
    var showsProgress: Bool {
        let needsAttention = ["failed", "needs-input"].contains(inspection?.run?.status ?? "")
        return isWorking || needsAttention || showsTelemetry
    }
    var viewingDescription: String {
        selectedRun == "latest" ? "Following latest results" : "Viewing saved run \(selectedRun.prefix(8))"
    }
    var savedResultsNotice: String {
        inspection?.run?.status == "running"
            ? "Showing saved results while this run validates reusable work."
            : "Showing completed results from an earlier run. Stopping processing keeps saved work available."
    }
    var geographicRun: RunData? {
        guard let run = displayedRun else { return nil }
        for stage in [PipelineStage.export, .texture, .mesh, .alignment] {
            if run.artifacts[stage.rawValue] != nil { return savedArtifactSources[stage] ?? run }
        }
        return run
    }
    var savedStageCount: Int {
        PipelineStage.allCases.filter { workingRun?.artifacts[$0.rawValue] != nil }.count
    }
    var progressTitle: String {
        if isWorking, let stage = workingRun?.progress.flatMap({ PipelineStage(rawValue: $0.stage) }) {
            if workingRun?.progress?.phase == "checking" {
                let target = workingRun.flatMap { PipelineStage(rawValue: $0.target) }?.title ?? "next stage"
                return "Preparing \(target) · checking saved \(stage.title)"
            }
            return "\(stage.title) · stage \((PipelineStage.allCases.firstIndex(of: stage) ?? 0) + 1) of 9"
        }
        if isWorking { return "Working…" }
        switch inspection?.run?.status {
        case "completed": return "Processing complete"
        case "failed", "needs-input": return "Processing needs attention"
        case "cancelled": return "Processing stopped · saved work retained"
        default: return "Ready to process"
        }
    }
    var progressMessage: String {
        if cancelRequested {
            return "Stopping… Your saved results will be kept."
        }
        if isWorking { return workingRun?.progress?.message ?? status }
        if inspection?.run?.status == "cancelled" {
            return "Saved results are still available. Resume reuses saved work and repeats any unfinished steps."
        }
        if let error = inspection?.run?.error { return error }
        return inspection?.run == nil
            ? "Choose a stage, then Process to that stage. Earlier stages run automatically."
            : "Choose an artifact in the workspace tree to inspect its saved output."
    }

    var progressActivity: String {
        if isWorking, !cancelRequested, let title = workingRun?.progress?.stepActivityTitle {
            return title
        }
        guard isWorking, !cancelRequested, let progress = workingRun?.progress,
            let operation = progress.operation, !operation.isEmpty, progress.phase != "checking"
        else { return progressTitle }
        return operation
    }

    var progressDetail: String {
        if cancelRequested { return "Stopping… Your saved results will be kept." }
        if isWorking, workingRun?.progress?.phase == "checking" {
            return
                "Validating saved checkpoints. Existing results remain available; reconstruction is reused when compatible."
        }
        if isWorking, let progress = workingRun?.progress, let counts = progress.countSummary {
            return counts
        }
        return progressMessage
    }

    var firstOutdatedStage: PipelineStage? {
        guard let project = inspection?.project, let saved = inspection?.run?.snapshot else { return nil }
        return ProcessingImpact().firstChangedStage(from: saved, to: project)
    }

    func stageStatus(_ stage: PipelineStage) -> String {
        if let firstOutdatedStage, ProcessingImpact().stages(from: firstOutdatedStage).contains(stage),
            displayedRun?.artifacts[stage.rawValue] != nil
        {
            return "Out of date"
        }
        if retainedStages.contains(stage) {
            return inspection?.run?.status == "running" ? "Saved · awaiting validation" : "Saved from earlier run"
        }
        if displayedRun?.artifacts[stage.rawValue] != nil { return "Saved" }
        if inspection?.run?.artifacts[stage.rawValue + "Preview"] != nil {
            return inspection?.run?.status == "running" ? "Live preview" : "Partial preview"
        }
        if inspection?.run?.status == "running", inspection?.run?.progress?.stage == stage.rawValue {
            return "Processing"
        }
        return "Not processed"
    }

    func stageSymbol(_ stage: PipelineStage) -> String {
        switch stageStatus(stage) {
        case "Out of date": return "exclamationmark.triangle.fill"
        case "Saved", "Saved · awaiting validation", "Saved from earlier run": return "checkmark.circle.fill"
        case "Live preview", "Processing": return "arrow.trianglehead.2.clockwise.rotate.90"
        case "Partial preview": return "circle.lefthalf.filled"
        default: return "circle"
        }
    }

    func stageColor(_ stage: PipelineStage) -> Color {
        switch stageStatus(stage) {
        case "Out of date": return .orange
        case "Saved", "Saved · awaiting validation", "Saved from earlier run": return .green
        case "Live preview", "Processing": return .accentColor
        default: return .secondary
        }
    }

    func copyText(_ value: String) { clipboard.copy(value) }

    func latestFeatureRun() async throws -> RunData? {
        try await client.inspect(root: root, run: "latest").run
    }

    func exportGeoreference(root: String, runID: String, destination: URL) async throws {
        try await client.command(
            root: root, arguments: ["export-gis", "--run", runID, "--output", destination.path], progress: { _ in })
    }

    func openMap(for photo: Photo? = nil) {
        if let photo {
            selectedPhotoID = photo.id
            if !filteredPhotos.contains(where: { $0.id == photo.id }) {
                query = ""
                selectedSet = "All sets"
                locationFilter = "All images"
            }
        }
        sheet = nil
        destination = .images
        imagesViewMode = .map
    }

    func metadata(for photo: Photo) async throws -> [String: JSONValue] {
        try await client.metadata(root: root, imageID: photo.id)
    }

    func metadataPayload(for photo: Photo, tag: String) async throws -> Data {
        try await client.metadataPayload(root: root, imageID: photo.id, tag: tag)
    }

    var filteredPhotos: [Photo] {
        (inspection?.project.images ?? []).filter { image in
            let matchesQuery =
                query.isEmpty || image.name.localizedCaseInsensitiveContains(query)
                || image.camera.localizedCaseInsensitiveContains(query)
            let matchesSet = selectedSet == "All sets" || image.sets.contains(selectedSet)
            let matchesLocation: Bool
            switch locationFilter {
            case "GPS present": matchesLocation = image.gps != nil
            case "No GPS": matchesLocation = image.gps == nil
            case "Discarded": matchesLocation = image.isDiscarded
            case "Disabled": matchesLocation = !image.enabled
            case "Unplaced": matchesLocation = inspection?.reconstruction?.unplaced.contains(image.id) == true
            default: matchesLocation = true
            }
            return matchesQuery && matchesSet && matchesLocation
        }
    }

    func poll() async {
        while !Task.isCancelled {
            await reload()
            let discoveringImages = isWorking && ["features", "depth"].contains(workingRun?.progress?.stage ?? "")
            try? await Task.sleep(for: .seconds(discoveringImages ? 1 : 3))
        }
    }

    func reload() async {
        guard !root.isEmpty, !refreshing else { return }
        refreshing = true
        defer { refreshing = false }
        let requestedRoot = root
        let requestedRun = selectedRun
        do {
            async let summaries = client.runs(root: requestedRoot)
            async let snapshot = client.inspect(root: requestedRoot, run: requestedRun)
            let result = try await (summaries, snapshot)
            guard requestedRoot == root, requestedRun == selectedRun else { return }
            if let run = result.1.run, requestedRun == "latest" {
                let key = requestedRoot + "/" + run.id
                if key != savedRunKey {
                    var previous: [RunData] = []
                    // Latest may be an older run being resumed. Explicit history views stay unchanged.
                    for summary in result.0 where summary.id != run.id && summary.status != "running" {
                        if let saved = try? await client.run(root: requestedRoot, id: summary.id) {
                            previous.append(saved)
                        }
                    }
                    guard requestedRoot == root, requestedRun == selectedRun else { return }
                    savedRuns = previous
                    savedRunKey = key
                }
                let projectURL = URL(fileURLWithPath: requestedRoot)
                let presentation = SavedArtifactPresentation(current: run, candidates: savedRuns) { path in
                    FileManager.default.fileExists(
                        atPath: projectURL.appendingPathComponent(path).appendingPathComponent("manifest.json").path)
                }
                displayedRun = presentation.run
                retainedStages = presentation.retainedStages
                savedArtifactSources = presentation.sourceRuns
            } else {
                displayedRun = result.1.run
                retainedStages = []
                savedRuns = []
                savedArtifactSources = [:]
                savedRunKey = ""
            }
            runs = result.0
            inspection = result.1
            openingProject = false
            openingError = nil
            if recordedRoot != requestedRoot {
                recentProjects = recentProjectStore.record(path: requestedRoot, name: result.1.project.name)
                recordedRoot = requestedRoot
            }
            if let active = activeRun {
                workingRun =
                    active.id == result.1.run?.id
                    ? result.1.run : try await client.run(root: requestedRoot, id: active.id)
            } else {
                workingRun = nil
                cancelRequested = false
                cancellationRequestedAt = nil
            }
            let measuredRun = workingRun ?? result.1.run
            telemetry.record(root: requestedRoot, runID: measuredRun?.id, resources: measuredRun?.progress?.resources)
            if let run = result.1.run {
                replaceLogs(try await logReader.read(root: requestedRoot, runID: run.id))
            } else {
                replaceLogs([])
            }
            commands = await commandLog?.entries(root: requestedRoot) ?? []
            commandLogError = await commandLog?.persistenceError(root: requestedRoot)
            if !busy {
                status = result.1.run.map { "\($0.target.capitalized) · \($0.status)" } ?? "Ready to import photos."
            }
        } catch {
            guard requestedRoot == root, requestedRun == selectedRun else { return }
            status = error.localizedDescription
            openingProject = false
            if inspection == nil { openingError = error.localizedDescription }
        }
    }

    func openProject() {
        guard canEdit else { return }
        let panel = NSOpenPanel()
        panel.title = "Open Echo project"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        open(path: url.path)
    }

    func openRecent(_ project: RecentProject) {
        guard canEdit else { return }
        open(path: project.path)
    }

    func removeRecent(_ project: RecentProject) {
        if root == project.path, inspection == nil { clearProject() }
        recentProjects = recentProjectStore.remove(path: project.path)
        openingError = nil
    }

    func renameRecent(_ project: RecentProject, name: String) async throws {
        guard canEdit, !openingProject else { throw CLIError(message: "Wait for the current operation to finish.") }
        busy = true
        defer { busy = false }
        try await client.command(
            root: project.path, arguments: ["project", "rename", "--name", name], progress: { _ in })
        recentProjects = recentProjectStore.rename(path: project.path, name: name)
        openingError = nil
    }

    func deleteRecent(_ project: RecentProject) {
        guard canEdit, !openingProject else { return }
        busy = true
        openingError = nil
        Task {
            defer { busy = false }
            do {
                guard let data = try await projectForDeletion(project) else { return }
                let alert = NSAlert()
                alert.messageText = "Delete “\(project.name)” from disk?"
                alert.informativeText =
                    "This permanently deletes the entire project folder, including imported photos, saved runs and meshes. This cannot be undone.\n\n\(project.path)"
                alert.alertStyle = .warning
                alert.addButton(withTitle: "Cancel")
                alert.addButton(withTitle: "Delete from Disk").hasDestructiveAction = true
                guard alert.runModal() == .alertSecondButtonReturn else { return }
                try await client.command(
                    root: project.path, arguments: ["project", "delete", "--confirm", data.id],
                    progress: { _ in })
                removeRecent(project)
            } catch {
                openingError = error.localizedDescription
            }
        }
    }

    func projectForDeletion(_ project: RecentProject) async throws -> ProjectData? {
        let folder = URL(fileURLWithPath: project.path)
        if !FileManager.default.fileExists(atPath: folder.path) {
            removeRecent(project)
            return nil
        }
        guard FileManager.default.fileExists(atPath: folder.appendingPathComponent("project.json").path) else {
            throw CLIError(
                message:
                    "This folder no longer contains an Echo project. Remove it from recents, or use Finder to manage its remaining files."
            )
        }
        return try await client.project(root: project.path)
    }

    func showProjects() {
        guard canEdit else { return }
        clearProject()
    }

    private func clearProject() {
        openingProject = false
        destination = .images
        imagesViewMode = .tiles
        root = ""
        workingRun = nil
        telemetry.record(root: root, runID: nil, resources: nil)
        showsTelemetry = false
        inspection = nil
        displayedRun = nil
        retainedStages = []
        savedRuns = []
        savedArtifactSources = [:]
        savedRunKey = ""
        runs = []
        replaceLogs([])
        commands = []
        commandLogError = nil
        selectedPhotoID = nil
        openingError = nil
        recordedRoot = ""
    }

    private func open(path: String) {
        root = path
        destination = .images
        imagesViewMode = .tiles
        query = ""
        selectedSet = "All sets"
        locationFilter = "All images"
        selectedRun = "latest"
        selectedPhotoID = nil
        inspection = nil
        displayedRun = nil
        retainedStages = []
        savedRuns = []
        savedArtifactSources = [:]
        savedRunKey = ""
        runs = []
        workingRun = nil
        telemetry.record(root: root, runID: nil, resources: nil)
        showsTelemetry = false
        replaceLogs([])
        commands = []
        commandLogError = nil
        openingProject = true
        openingError = nil
        Task { await reload() }
    }

    func createProject() {
        guard canEdit else { return }
        let panel = NSSavePanel()
        panel.title = "Create Echo project"
        panel.nameFieldStringValue = "Cliff.echo"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        perform("Creating project…") {
            try await self.client.command(root: nil, arguments: ["project", "init", url.path], progress: { _ in })
            self.root = url.path
            self.selectedRun = "latest"
        }
    }

    func importPhotos() {
        guard canEdit, inspection != nil else { return }
        let panel = NSOpenPanel()
        panel.title = "Import a photo folder"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        execute("Importing photos…", ["images", "import", url.path, "--set", url.lastPathComponent])
    }

    func start(through stage: PipelineStage? = nil) {
        guard canEdit else { return }
        selectedRun = "latest"
        execute(
            "Processing saved inputs…",
            ["run", "start", "--target", (stage ?? selectedStage).rawValue, "--events", "ndjson"])
    }

    func resume() {
        guard canEdit, let run = inspection?.run else { return }
        execute("Resuming saved run…", ["run", "resume", run.id, "--events", "ndjson"])
    }

    func cancel() {
        guard let run = activeRun else { return }
        cancelRequested = true
        cancellationRequestedAt = Date()
        Task {
            do {
                try await client.command(root: root, arguments: ["run", "cancel", run.id], progress: { _ in })
                status = "Stop requested. Checking worker state…"
                await reload()
            } catch {
                cancelRequested = false
                cancellationRequestedAt = nil
                errorMessage = error.localizedDescription
            }
        }
    }

    func stopForTermination() async throws {
        quitting = true
        defer { quitting = false }
        status = "Stopping processing before quitting…"
        var requested = Set<String>()
        while true {
            let active = root.isEmpty ? [] : try await client.runs(root: root).filter { $0.status == "running" }
            for run in active where !requested.contains(run.id) {
                do {
                    try await client.command(root: root, arguments: ["run", "cancel", run.id], progress: { _ in })
                    requested.insert(run.id)
                    cancelRequested = true
                    cancellationRequestedAt = Date()
                } catch {
                    // The job can finish between listing it and requesting cancellation.
                    let current = try await client.runs(root: root)
                    if current.contains(where: { $0.id == run.id && $0.status == "running" }) { throw error }
                }
            }
            // busy covers startup before a run is saved, and waiting for our CLI process to exit.
            if !busy && active.isEmpty { return }
            try await Task.sleep(for: .milliseconds(250))
        }
    }

    func trimSimilarPhotos() {
        execute("Trimming similar photos…", ["images", "deduplicate", "--apply"])
    }

    func setEnabled(_ photo: Photo, enabled: Bool) {
        if enabled && photo.isDiscarded {
            execute("Restoring photo…", ["images", "restore", photo.id])
            return
        }
        requestEdit("Change processing images?", from: .features) {
            self.execute("Updating image…", ["images", "enable", photo.id, "--value", String(enabled)])
        }
    }

    func save(settings: ProcessingSettings) {
        perform("Saving processing settings…") {
            try await self.client.importValue(root: self.root, command: "config", value: settings)
        }
    }

    func save(hint: ImageHint) {
        requestEdit("Change reconstruction hints?", from: .matching) {
            self.perform("Saving image hint…") {
                try await self.client.importValue(root: self.root, command: "hints", value: [hint])
            }
        }
    }

    func disable(hint: ImageHint) {
        requestEdit("Disable this hint?", from: .matching) {
            self.execute("Disabling hint…", ["hints", "disable", hint.id])
        }
    }

    private func requestEdit(_ title: String, from stage: PipelineStage, action: @escaping () -> Void) {
        guard canEdit else { return }
        guard inspection?.run != nil else {
            action()
            return
        }
        pendingChange = ProjectEditRequest(
            title: title, message: ProcessingImpact().warning(from: stage), apply: action)
    }

    func applyPendingEdit() {
        let action = pendingChange?.apply
        pendingChange = nil
        action?()
    }

    func loadStorage() {
        perform("Inspecting generated files…") {
            self.storageInventory = try await self.client.storage(root: self.root)
        }
    }

    func prepareDeletion(kind: ArtifactDeletionKind, artifact: String? = nil) {
        let projectRoot = root
        perform("Preparing deletion preview…") {
            let selection: ArtifactDeletionSelection
            switch kind {
            case .unused:
                selection = .unused(retaining: try await self.cleanupRetainedRuns(root: projectRoot))
            case .artifact:
                guard let artifact else { throw CLIError(message: "Select an artifact to delete.") }
                selection = .artifact(artifact)
            case .allGenerated:
                selection = .allGenerated
            }
            let reviewedSelection = selection
            let plan = try await self.client.deletionPlan(root: projectRoot, selection: reviewedSelection)
            let logCutoff = try plan.logCutoffDate
            self.pendingChange = ProjectEditRequest(
                title: plan.kind.title,
                message: plan.warning,
                apply: {
                    guard self.root == projectRoot else { return }
                    self.perform("Deleting generated results…") {
                        let arguments =
                            ["storage", "delete", "--confirm", plan.token]
                            + reviewedSelection.arguments
                        try await self.client.command(root: projectRoot, arguments: arguments, progress: { _ in })
                        if let logCutoff {
                            try await self.commandLog?.prune(root: projectRoot, before: logCutoff)
                        }
                        self.selectedRun = "latest"
                        self.savedRunKey = ""
                        self.storageInventory = try await self.client.storage(root: projectRoot)
                    }
                }, actionTitle: plan.kind.actionTitle, destructive: true)
        }
    }

    private func cleanupRetainedRuns(root: String) async throws -> [String] {
        let summaries = try await client.runs(root: root)
        guard let latest = summaries.first else { return [] }
        let current = try await client.run(root: root, id: latest.id)
        var candidates: [RunData] = []
        for saved in summaries.dropFirst() {
            candidates.append(try await client.run(root: root, id: saved.id))
        }
        let project = URL(fileURLWithPath: root)
        let presentation = SavedArtifactPresentation(current: current, candidates: candidates) { path in
            FileManager.default.fileExists(
                atPath: project.appendingPathComponent(path).appendingPathComponent("manifest.json").path)
        }
        return Array(Set([current.id] + presentation.sourceRuns.values.map(\.id))).sorted()
    }

    func reset(from stage: PipelineStage) {
        guard canEdit else { return }
        pendingChange = ProjectEditRequest(
            title: "Reset from \(stage.shortTitle)?",
            message: ProcessingImpact().warning(from: stage)
                + " Next time you press Process, these stages will be computed again. Earlier checkpoints are reused; no photographs or saved runs are deleted.",
            apply: {
                self.execute("Resetting stages…", ["pipeline", "reset", "--from", stage.rawValue])
            }, actionTitle: "Reset stages")
    }

    func showArtifact(_ stage: PipelineStage) {
        selectedStage = stage
    }

    func revealArtifact(_ stage: PipelineStage) {
        guard let path = displayedRun?.artifacts[stage.rawValue] else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: root).appendingPathComponent(path))
    }

    private func execute(_ title: String, _ arguments: [String]) {
        perform(title) {
            try await self.client.command(root: self.root, arguments: arguments) { line in
                Task { @MainActor in self.receive(line) }
            }
        }
    }

    private func perform(_ title: String, operation: @escaping @MainActor () async throws -> Void) {
        guard canEdit else { return }
        busy = true
        errorMessage = nil
        status = title
        Task {
            do {
                try await operation()
                status = "Completed."
            } catch is CancellationError {
                status = "Processing stopped. Saved results are still available."
            } catch {
                status = "Operation stopped. Completed artifacts are retained."
                if !quitting { errorMessage = error.localizedDescription }
            }
            busy = false
            await reload()
            if title == "Saving processing settings…", errorMessage == nil { status = "Processing settings saved." }
            if title == "Trimming similar photos…", errorMessage == nil {
                let enabled = inspection?.project.images.filter(\.enabled).count ?? 0
                status =
                    "Similar photos trimmed. \(enabled) photos enabled. Press Process to use this selection; Resume keeps the saved run’s selection."
            }
        }
    }

    private func replaceLogs(_ entries: [RunLogEntry]) {
        guard logs != entries else { return }
        logIDs = Set(entries.map(\.id))
        logs = entries
    }

    private func receive(_ line: String) {
        guard let data = line.data(using: .utf8),
            let record = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return }
        if let name = record["name"] as? String { status = "Imported \(name)" }
        if let entry = eventFormatter.entry(from: data) {
            status = entry.message
            if selectedRun == "latest" || selectedRun == inspection?.run?.id {
                if logIDs.insert(entry.id).inserted { logs.append(entry) }
            }
        }
    }
}
