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
    private var refreshing = false

    @Published var recentProjects: [RecentProject]
    @Published var openingProject = false
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
    @Published var selectedStage = PipelineStage.alignment
    @Published var sheet: WorkspaceSheet?
    @Published var mapFocusPhotoID: String?
    @Published var logs: [RunLogEntry] = []
    @Published var workingRun: RunData?
    @Published var cancelRequested = false
    @Published var cancellationRequestedAt: Date?
    @Published var busy = false
    @Published private(set) var quitting = false
    @Published var status = "Open or create a project to begin."
    @Published var errorMessage: String?
    @Published var pendingChange: ProjectEditRequest?
    @Published var recentEvents: [String] = []

    init(
        client: ProjectClient, project: String, logReader: RunLogReader, eventFormatter: RunEventFormatter,
        recentProjectStore: RecentProjectStore, clipboard: TextClipboard
    ) {
        self.clipboard = clipboard
        self.recentProjectStore = recentProjectStore
        recentProjects = recentProjectStore.load()
        self.logReader = logReader
        self.eventFormatter = eventFormatter
        self.client = client
        root = project
    }

    var selectedPhoto: Photo? {
        inspection?.project.images.first { $0.id == selectedPhotoID }
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
        return isWorking || needsAttention
    }
    var viewingDescription: String {
        selectedRun == "latest" ? "Following latest results" : "Viewing saved run \(selectedRun.prefix(8))"
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
            return "Stop requested. Completed checkpoints are retained; unfinished work will be discarded."
        }
        if isWorking { return workingRun?.progress?.message ?? status }
        if let error = inspection?.run?.error { return error }
        return inspection?.run == nil
            ? "Choose a stage, then Process to that stage. Earlier stages run automatically."
            : "Choose a stage tab to inspect its saved output."
    }

    var firstOutdatedStage: PipelineStage? {
        guard let project = inspection?.project, let saved = inspection?.run?.snapshot else { return nil }
        return ProcessingImpact().firstChangedStage(from: saved, to: project)
    }

    func stageStatus(_ stage: PipelineStage) -> String {
        if let firstOutdatedStage, ProcessingImpact().stages(from: firstOutdatedStage).contains(stage),
            inspection?.run?.artifacts[stage.rawValue] != nil
        {
            return "Out of date"
        }
        if inspection?.run?.artifacts[stage.rawValue] != nil { return "Saved" }
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
        case "Saved": return "checkmark.circle.fill"
        case "Live preview", "Processing": return "arrow.trianglehead.2.clockwise.rotate.90"
        case "Partial preview": return "circle.lefthalf.filled"
        default: return "circle"
        }
    }

    func stageColor(_ stage: PipelineStage) -> Color {
        switch stageStatus(stage) {
        case "Out of date": return .orange
        case "Saved": return .green
        case "Live preview", "Processing": return .accentColor
        default: return .secondary
        }
    }

    func copyText(_ value: String) { clipboard.copy(value) }

    func latestFeatureRun() async throws -> RunData? {
        try await client.inspect(root: root, run: "latest").run
    }

    func openMap(for photo: Photo? = nil) {
        mapFocusPhotoID = photo?.id
        if let photo { selectedPhotoID = photo.id }
        sheet = .map
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
            let findingFeatures = isWorking && workingRun?.progress?.stage == "features"
            try? await Task.sleep(for: .seconds(findingFeatures ? 1 : 3))
        }
    }

    func reload() async {
        guard !root.isEmpty, !refreshing else { return }
        refreshing = true
        defer { refreshing = false }
        do {
            let requestedRoot = root
            let requestedRun = selectedRun
            async let summaries = client.runs(root: requestedRoot)
            async let snapshot = client.inspect(root: requestedRoot, run: requestedRun)
            let result = try await (summaries, snapshot)
            guard requestedRoot == root, requestedRun == selectedRun else { return }
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
            if let run = result.1.run {
                logs = try await logReader.read(root: requestedRoot, runID: run.id)
            } else {
                logs = []
            }
            if !busy {
                status = result.1.run.map { "\($0.target.capitalized) · \($0.status)" } ?? "Ready to import photos."
            }
        } catch {
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
        recentProjects = recentProjectStore.remove(path: project.path)
    }

    func showProjects() {
        guard canEdit else { return }
        root = ""
        workingRun = nil
        inspection = nil
        runs = []
        logs = []
        selectedPhotoID = nil
        openingError = nil
        recordedRoot = ""
    }

    private func open(path: String) {
        root = path
        selectedRun = "latest"
        selectedPhotoID = nil
        inspection = nil
        runs = []
        workingRun = nil
        logs = []
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

    func setEnabled(_ photo: Photo, enabled: Bool) {
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

    func prepareDeletion(artifact: String?) {
        let projectRoot = root
        perform("Preparing deletion preview…") {
            let plan = try await self.client.deletionPlan(root: projectRoot, artifact: artifact)
            self.pendingChange = ProjectEditRequest(
                title: plan.purgeAll ? "Purge all generated results?" : "Delete artifact and dependent indexes?",
                message: plan.warning,
                apply: {
                    guard self.root == projectRoot else { return }
                    self.perform("Deleting generated results…") {
                        let arguments =
                            ["storage", "delete", "--confirm", plan.token]
                            + (artifact.map { ["--artifact", $0] } ?? [])
                        try await self.client.command(root: projectRoot, arguments: arguments, progress: { _ in })
                        self.selectedRun = "latest"
                        self.storageInventory = try await self.client.storage(root: projectRoot)
                    }
                }, actionTitle: plan.purgeAll ? "Purge all results" : "Delete artifact", destructive: true)
        }
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
        guard let path = inspection?.run?.artifacts[stage.rawValue] else { return }
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
            } catch {
                status = "Operation stopped. Completed artifacts are retained."
                if !quitting { errorMessage = error.localizedDescription }
            }
            busy = false
            await reload()
            if title == "Saving processing settings…", errorMessage == nil { status = "Processing settings saved." }
        }
    }

    private func receive(_ line: String) {
        recentEvents.append(line)
        if recentEvents.count > 150 { recentEvents.removeFirst() }
        guard let data = line.data(using: .utf8),
            let record = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return }
        if let name = record["name"] as? String { status = "Imported \(name)" }
        if let entry = eventFormatter.entry(from: data) {
            status = entry.message
            if selectedRun == "latest" || selectedRun == inspection?.run?.id {
                if !logs.contains(where: { $0.id == entry.id }) { logs.append(entry) }
            }
        }
    }
}
