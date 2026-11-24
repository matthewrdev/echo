import SwiftUI

struct DroneImportView: View {
    @ObservedObject var model: DroneImportViewModel
    let source: DroneSource
    let didCreate: (DroneImportProject) -> Void
    let openProject: (DroneImportProject) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var destination: URL?
    @State private var confirmsImport = false
    @State private var cleanup: DroneCleanupMode?
    @State private var confirmsCleanup = false

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Label("Drone Import", systemImage: "sdcard").font(.title2.bold())
                Spacer()
                Text(source.name).foregroundStyle(.secondary)
            }
            .padding(16)
            Divider()
            if model.finished {
                results
            } else if model.photos.isEmpty {
                emptyState
            } else {
                DroneImportEditor(model: model).disabled(model.busy)
            }
            Divider()
            footer
        }
        .frame(minWidth: 940, idealWidth: 1100, minHeight: 620, idealHeight: 700)
        .task(id: source.id) { await model.scan(source) }
        .interactiveDismissDisabled(model.busy)
        .alert("Create Echo projects?", isPresented: $confirmsImport) {
            Button("Cancel", role: .cancel) {}
            Button("Create Projects") {
                guard let destination else { return }
                Task { await model.createProjects(from: source, in: destination, didCreate: didCreate) }
            }
        } message: {
            Text(importSummary)
        }
        .alert(cleanup?.title ?? "Delete Camera Files", isPresented: $confirmsCleanup, presenting: cleanup) { mode in
            Button("Cancel", role: .cancel) {}
            Button("Delete Files", role: .destructive) {
                Task { await model.clean(mode, source: source) }
            }
        } message: { mode in
            Text(cleanupSummary(mode))
        }
    }

    private var emptyState: some View {
        Group {
            if model.busy {
                ProgressView(model.progress ?? "Reading camera storage…")
            } else {
                ContentUnavailableView(
                    "No photos to review", systemImage: "photo.on.rectangle",
                    description: Text("Reconnect the source and try again."))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var results: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("\(model.projects.count.formatted()) projects created").font(.headline)
            List(model.projects) { project in
                HStack {
                    Label(project.name, systemImage: "folder")
                    Spacer()
                    Text("\(project.receipts.count.formatted()) photos").foregroundStyle(.secondary)
                    Button("Show in Finder") {
                        NSWorkspace.shared.activateFileViewerSelecting([project.url])
                    }
                    Button("Open in Echo") {
                        dismiss()
                        openProject(project)
                    }
                }
            }
            .listStyle(.inset)
            Divider()
            HStack {
                Text("Files stay on \(source.name) unless you delete them here.")
                    .font(.callout).foregroundStyle(.secondary)
                Spacer()
                Button("Delete Imported Photos…") { askToClean(.imported) }
                    .disabled(model.cleanupFiles(for: .imported).isEmpty)
                Button("Clear Camera Media…") { askToClean(.allMedia) }
                    .disabled(model.cleanupFiles(for: .allMedia).isEmpty)
            }
        }
        .padding(16)
        .disabled(model.busy)
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let message = model.message {
                Text(message).font(.callout).textSelection(.enabled)
            }
            HStack {
                if let progress = model.progress, !model.photos.isEmpty {
                    ProgressView().controlSize(.small)
                    Text(progress).lineLimit(1)
                } else if !model.finished {
                    Text(
                        "\(model.pendingGroups.count.formatted()) projects · \(model.pendingPhotos.count.formatted()) photos · \(model.unassignedCount.formatted()) unassigned · \(model.excluded.count.formatted()) excluded"
                    )
                    .foregroundStyle(.secondary)
                }
                Spacer()
                Button(model.finished ? "Done" : "Close") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                    .disabled(model.busy)
                if !model.finished && model.pendingGroups.isEmpty && !model.projects.isEmpty {
                    Button("Review Created Projects", action: model.reviewCreatedProjects)
                        .buttonStyle(.borderedProminent)
                        .disabled(model.busy)
                } else if !model.finished {
                    Button("Create Projects…", action: chooseDestination)
                        .buttonStyle(.borderedProminent)
                        .disabled(model.busy || model.pendingGroups.isEmpty)
                }
            }
        }
        .padding(12)
    }

    private var importSummary: String {
        let counts = Dictionary(grouping: model.pendingPhotos) { model.assignments[$0.id] }
        let previews = model.pendingGroups.prefix(8).map { group in
            "\(group.name) · \((counts[group.id]?.count ?? 0).formatted()) photos"
        }
        let remaining = model.pendingGroups.count - previews.count
        let overflow = remaining > 0 ? "\nAnd \(remaining.formatted()) more projects" : ""
        let folder = destination?.path ?? ""
        let skipped =
            "\(model.unassignedCount.formatted()) unassigned and \(model.excluded.count.formatted()) excluded photos will be skipped."
        return "\(folder)\n\n\(previews.joined(separator: "\n"))\(overflow)\n\n\(skipped)"
    }

    private func chooseDestination() {
        let panel = NSOpenPanel()
        panel.title = "Choose where to save the Echo projects"
        panel.prompt = "Review"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        destination = url
        confirmsImport = true
    }

    private func askToClean(_ mode: DroneCleanupMode) {
        cleanup = mode
        confirmsCleanup = true
    }

    private func cleanupSummary(_ mode: DroneCleanupMode) -> String {
        let count = model.cleanupFiles(for: mode).count.formatted()
        switch mode {
        case .imported:
            return
                "Permanently delete up to \(count) imported photos from \(source.name) after rechecking the saved copies?"
        case .allMedia:
            return
                "Permanently delete up to \(count) files found in the camera folders on \(source.name)? This includes excluded and unassigned photos, RAW files, and videos that Echo may not have copied."
        }
    }
}
