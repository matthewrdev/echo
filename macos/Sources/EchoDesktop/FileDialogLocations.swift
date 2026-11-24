import AppKit

struct FileDialogLocations {
    private let defaults: UserDefaults

    init(defaults: UserDefaults) {
        self.defaults = defaults
    }

    @MainActor
    func choose(_ panel: NSSavePanel, for feature: FileDialogFeature, fallback: URL? = nil) -> URL? {
        panel.directoryURL = startingDirectory(for: feature, fallback: fallback)
        guard panel.runModal() == .OK, let selection = panel.url else { return nil }
        remember(selection, for: feature)
        return selection
    }

    func startingDirectory(for feature: FileDialogFeature, fallback: URL? = nil) -> URL {
        let saved = defaults.string(forKey: key(for: feature)).map { URL(fileURLWithPath: $0, isDirectory: true) }
        return [saved, fallback, URL.documentsDirectory].compactMap { $0 }.first {
            (try? $0.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true
        } ?? URL.homeDirectory
    }

    func remember(_ selection: URL, for feature: FileDialogFeature) {
        defaults.set(feature.directory(for: selection).path, forKey: key(for: feature))
    }

    private func key(for feature: FileDialogFeature) -> String {
        "fileDialog.directory.\(feature.rawValue)"
    }
}
