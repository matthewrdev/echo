import Combine
import Foundation

@MainActor
final class DroneImportViewModel: ObservableObject {
    private let service: DroneImportService
    private var media: [DroneMediaFile] = []
    private var removed: Set<URL> = []

    @Published private(set) var photos: [DroneImportPhoto] = []
    @Published private(set) var groups: [DroneImportGroup] = []
    @Published private(set) var assignments: [URL: UUID] = [:]
    @Published private(set) var excluded: Set<URL> = []
    @Published private(set) var projects: [DroneImportProject] = []
    @Published private(set) var busy = false
    @Published private(set) var finished = false
    @Published private(set) var progress: String?
    @Published var selection: Set<URL> = []
    @Published var message: String?

    init(service: DroneImportService) {
        self.service = service
    }

    var reader: DroneSourceReader { service.reader }

    var pendingPhotos: [DroneImportPhoto] {
        let completed = Set(projects.map(\.id))
        return photos.filter { photo in
            !excluded.contains(photo.id) && assignments[photo.id].map { !completed.contains($0) } == true
        }
    }

    var pendingGroups: [DroneImportGroup] {
        let assigned = Set(pendingPhotos.compactMap { assignments[$0.id] })
        return groups.filter { assigned.contains($0.id) }
    }

    var unassignedCount: Int { photos.count { assignments[$0.id] == nil && !excluded.contains($0.id) } }

    var editablePhotos: [DroneImportPhoto] {
        let completed = Set(projects.map(\.id))
        return photos.filter { assignments[$0.id].map { !completed.contains($0) } ?? true }
    }

    func scan(_ source: DroneSource) async {
        photos = []
        groups = []
        assignments = [:]
        excluded = []
        projects = []
        selection = []
        media = []
        removed = []
        message = nil
        finished = false
        busy = true
        progress = "Reading camera storage…"
        defer {
            progress = nil
            busy = false
        }

        let reader = service.reader
        let scan = Task.detached(priority: .userInitiated) { try reader.scan(source) }
        do {
            let result = try await withTaskCancellationHandler {
                try await scan.value
            } onCancel: {
                scan.cancel()
            }
            guard !Task.isCancelled else { return }
            let suggestions = service.suggest(result.photos)
            photos = result.photos
            media = result.media
            groups = suggestions.groups
            assignments = suggestions.assignments
            if photos.isEmpty { message = "No supported photos are available on \(source.name)." }
        } catch is CancellationError {
            return
        } catch {
            message = error.localizedDescription
        }
    }

    @discardableResult
    func addGroup() -> UUID {
        var number = groups.count + 1
        while groups.contains(where: { $0.name == "Site \(number)" }) { number += 1 }
        let group = DroneImportGroup(name: "Site \(number)")
        groups.append(group)
        assignSelection(to: group.id)
        return group.id
    }

    func name(of group: UUID?) -> String {
        groups.first { $0.id == group }?.name ?? "Unassigned"
    }

    func rename(_ group: UUID, to name: String) {
        guard let index = groups.firstIndex(where: { $0.id == group }) else { return }
        groups[index].name = name
    }

    func assignSelection(to group: UUID?) {
        for id in selection { assignments[id] = group }
    }

    func setSelectionExcluded(_ skip: Bool) {
        if skip {
            excluded.formUnion(selection)
        } else {
            excluded.subtract(selection)
        }
    }

    func createProjects(
        from source: DroneSource, in folder: URL, didCreate: (DroneImportProject) -> Void
    ) async {
        guard !busy, !pendingGroups.isEmpty else { return }
        let names = pendingGroups.map { $0.name.trimmingCharacters(in: .whitespacesAndNewlines) }
        guard names.allSatisfy(validName) else {
            message = "Give each group a name without slashes, colons or a leading dot."
            return
        }
        let normalized = names.map { $0.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: .current) }
        guard Set(normalized).count == names.count else {
            message = "Use a different project name for each group."
            return
        }

        let candidates = pendingGroups
        let byGroup = Dictionary(grouping: pendingPhotos) { assignments[$0.id] }
        message = nil
        busy = true
        defer {
            progress = nil
            busy = false
        }

        for group in candidates {
            progress = "Creating \(group.name)…"
            do {
                let project = try await service.createProject(
                    group: group, photos: byGroup[group.id] ?? [], source: source, destination: folder
                ) { [weak self] line in
                    guard let event = try? JSONDecoder().decode(PhotoImportProgress.self, from: Data(line.utf8)),
                        event.isImportEvent
                    else { return }
                    Task { @MainActor [weak self] in
                        guard let self, self.busy, self.progress?.hasPrefix("Creating \(group.name)…") == true
                        else { return }
                        self.progress = "Creating \(group.name)… \(event.summary)"
                    }
                }
                projects.append(project)
                selection.subtract(project.receipts.map(\.file.id))
                didCreate(project)
            } catch {
                message = "\(group.name) · \(error.localizedDescription)"
                return
            }
        }
        finished = true
    }

    func cleanupFiles(for mode: DroneCleanupMode) -> [DroneMediaFile] {
        let candidates = mode == .imported ? projects.flatMap(\.receipts).map(\.file) : media
        return candidates.filter { !removed.contains($0.id) }
    }

    func reviewCreatedProjects() {
        guard !busy, pendingGroups.isEmpty, !projects.isEmpty else { return }
        finished = true
        message = nil
    }

    func clean(_ mode: DroneCleanupMode, source: DroneSource) async {
        guard finished, !busy else { return }
        let candidates = cleanupFiles(for: mode)
        let receipts = Dictionary(uniqueKeysWithValues: projects.flatMap(\.receipts).map { ($0.file.id, $0) })
        let service = self.service
        message = nil
        busy = true
        progress = "Deleting camera files…"
        let result = await Task.detached(priority: .userInitiated) {
            service.clean(candidates, on: source, verifying: receipts)
        }.value
        removed.formUnion(result.removed)
        let deleted = "Deleted \(result.removed.count.formatted()) of \(candidates.count.formatted()) files."
        message = ([deleted] + result.failures.prefix(5)).joined(separator: "\n")
        progress = nil
        busy = false
    }

    private func validName(_ name: String) -> Bool {
        !name.isEmpty && !name.hasPrefix(".") && name.utf8.count <= 250
            && name.rangeOfCharacter(from: CharacterSet(charactersIn: "/:\\").union(.controlCharacters)) == nil
    }
}
