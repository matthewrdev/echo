import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct TerrainInputView: View {
    let cli: CLIClient
    @Environment(\.dismiss) private var dismiss
    @State private var input: URL?
    @State private var model: URL?
    @State private var failure: String?
    @State private var building = false

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Build Terrain").font(.title2)
            Button(input?.lastPathComponent ?? "Choose Terrain Input…") { chooseInput() }
                .help(input?.path ?? "Choose a terrain JSON manifest.")
                .disabled(building)
            if building {
                ProgressView("Building terrain…").controlSize(.small)
            }
            if let failure {
                Text(failure).foregroundStyle(.red).textSelection(.enabled)
            }
            if let model {
                Button("Show Model in Finder") { NSWorkspace.shared.activateFileViewerSelecting([model]) }
            }
            HStack {
                Spacer()
                Button("Close") { dismiss() }.disabled(building)
                Button("Build…") { build() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(input == nil || building)
            }
        }
        .padding(20)
        .frame(minWidth: 400)
        .interactiveDismissDisabled(building)
    }

    private func chooseInput() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        input = url
        failure = nil
        model = nil
    }

    private func build() {
        guard let input else { return }
        let panel = NSSavePanel()
        panel.title = "Choose a New Output Folder"
        panel.nameFieldStringValue = "Terrain Export"
        panel.directoryURL = input.deletingLastPathComponent()
        guard panel.runModal() == .OK, let output = panel.url else { return }
        failure = nil
        model = nil
        building = true
        Task { @MainActor in
            defer { building = false }
            do {
                _ = try await cli.execute(["stitch-terrain", input.path, "--output", output.path], progress: { _ in })
                model = output.appendingPathComponent("cliff.glb")
            } catch {
                failure = error.localizedDescription
            }
        }
    }
}
