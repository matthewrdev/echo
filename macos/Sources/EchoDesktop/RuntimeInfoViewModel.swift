import Foundation

@MainActor
final class RuntimeInfoViewModel: ObservableObject {
    @Published private(set) var sections: [RuntimeInfoSection] = []
    @Published private(set) var isLoading = false
    @Published private(set) var error: String?
    @Published private(set) var updatedAt: Date?
    @Published private(set) var copied = false
    private let cli: CLIClient
    private let native: NativeRuntimeInfo
    private let clipboard: TextClipboard

    init(cli: CLIClient, native: NativeRuntimeInfo, clipboard: TextClipboard) {
        self.cli = cli
        self.native = native
        self.clipboard = clipboard
    }

    func refresh() async {
        guard !isLoading else { return }
        isLoading = true
        copied = false
        error = nil
        sections = native.read()
        updatedAt = Date()
        defer { isLoading = false }
        do {
            let data = try await cli.execute(["runtime-info"], progress: { _ in })
            let runtime = try JSONDecoder().decode(ProcessorRuntimeInfo.self, from: data)
            guard runtime.schemaVersion == 1 else { throw CLIError(message: "Unsupported runtime report version.") }
            sections += runtime.sections
        } catch {
            self.error = error.localizedDescription
        }
    }

    var report: String {
        var lines = ["Echo runtime information", "Captured: \(updatedAt?.ISO8601Format() ?? "Not yet captured")"]
        for section in sections {
            lines += ["", section.title]
            lines += section.fields.map { "\($0.name): \($0.value)" }
        }
        if let error { lines += ["", "Processor information unavailable: \(error)"] }
        return lines.joined(separator: "\n")
    }

    func copy() {
        clipboard.copy(report)
        copied = true
    }
}
