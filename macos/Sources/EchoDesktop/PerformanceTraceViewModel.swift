import AppKit
import Foundation
import UniformTypeIdentifiers

@MainActor
final class PerformanceTraceViewModel: ObservableObject {
    private let client: ProjectClient
    private var root = ""
    @Published var attempts: [PerformanceTraceSummary] = []
    @Published var selected = ""
    @Published var comparison = ""
    @Published var document: PerformanceTraceDocument?
    @Published var baseline: PerformanceTraceDocument?
    @Published var error: String?

    init(client: ProjectClient) { self.client = client }

    func refresh(root: String, preferredRun: String?) async {
        if self.root != root {
            self.root = root
            selected = ""
            comparison = ""
            document = nil
            baseline = nil
            attempts = []
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
        if comparing { baseline = nil } else if document?.summary.id != id { document = nil }
        guard let attempt = attempts.first(where: { $0.id == id }) else { return }
        let requestedRoot = root
        do {
            let value = try await client.performanceTrace(root: root, run: attempt.runId, attempt: id)
            guard requestedRoot == root, !Task.isCancelled,
                id == (comparing ? comparison : selected)
            else { return }
            guard value.schemaVersion == 1 else { throw CLIError(message: "Unsupported performance trace version.") }
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
        guard panel.runModal() == .OK, let file = panel.url else { return }
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(document).write(to: file, options: .atomic)
        } catch { self.error = error.localizedDescription }
    }
}
