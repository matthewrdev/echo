import AppKit
import Foundation
import UniformTypeIdentifiers

@MainActor
final class PerformanceTraceViewModel: ObservableObject {
    private let client: ProjectClient
    private let fileDialogLocations: FileDialogLocations
    private var root = ""
    private var historyStages: Set<String> = []
    @Published var attempts: [PerformanceTraceSummary] = []
    @Published var selected = ""
    @Published var comparison = ""
    @Published var document: PerformanceTraceDocument?
    @Published var baseline: PerformanceTraceDocument?
    @Published var originalTimings: [String: PerformanceStageTiming] = [:]
    @Published var error: String?

    init(client: ProjectClient, fileDialogLocations: FileDialogLocations) {
        self.client = client
        self.fileDialogLocations = fileDialogLocations
    }

    func refresh(root: String, preferredRun: String?) async {
        if self.root != root {
            self.root = root
            selected = ""
            comparison = ""
            document = nil
            baseline = nil
            attempts = []
            historyStages = []
            originalTimings = [:]
        }
        do {
            let found = try await client.performanceTraces(root: root)
            guard self.root == root, !Task.isCancelled else { return }
            attempts = found
            if !found.contains(where: { $0.id == selected }) {
                selected = found.first(where: { $0.runId == preferredRun })?.id ?? found.first?.id ?? ""
            }
            if let current = found.first(where: { $0.id == selected }),
                document?.summary.updatedAt != current.updatedAt || document?.summary.id != selected
            {
                await load()
            }
        } catch is CancellationError {
        } catch { self.error = error.localizedDescription }
    }

    func load(comparing: Bool = false) async {
        let id = comparing ? comparison : selected
        if comparing {
            baseline = nil
        } else if document?.summary.id != id {
            document = nil
            originalTimings = [:]
            historyStages = []
        }
        guard let attempt = attempts.first(where: { $0.id == id }) else { return }
        let requestedRoot = root
        do {
            let value = try await client.performanceTrace(root: root, run: attempt.runId, attempt: id)
            guard requestedRoot == root, !Task.isCancelled,
                id == (comparing ? comparison : selected)
            else { return }
            guard value.schemaVersion == 1 else { throw CLIError(message: "Unsupported performance trace version.") }
            if !comparing && (document?.summary.id != id || historyStages != value.reusedStages) {
                let timings = await PerformanceStageHistory(client: client).timings(
                    for: value, attempts: attempts, root: requestedRoot)
                guard requestedRoot == root, !Task.isCancelled, selected == id else { return }
                originalTimings = timings
                historyStages = value.reusedStages
            }
            if comparing { baseline = value } else { document = value }
            error = nil
        } catch is CancellationError {
        } catch { self.error = error.localizedDescription }
    }

    func export() {
        guard let document else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "echo-trace-\(document.summary.id).json"
        guard let file = fileDialogLocations.choose(panel, for: .performanceTraceExport) else { return }
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(document).write(to: file, options: .atomic)
        } catch { self.error = error.localizedDescription }
    }
}
