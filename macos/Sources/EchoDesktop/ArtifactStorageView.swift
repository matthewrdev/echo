import AppKit
import SwiftUI

struct ArtifactStorageView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var query = ""
    @State private var selected: String?

    private var items: [StoredArtifact] {
        (workspace.storageInventory?.artifacts ?? []).filter {
            query.isEmpty || $0.path.localizedCaseInsensitiveContains(query)
                || $0.runs.contains { $0.localizedCaseInsensitiveContains(query) }
        }.sorted { $0.bytes > $1.bytes }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let inventory = workspace.storageInventory {
                Text(
                    "\(inventory.artifacts.count.formatted()) artifacts · \(inventory.runCount) saved runs · \(ByteCountFormatter.string(fromByteCount: inventory.bytes, countStyle: .file)) including temporary files"
                )
            }
            HStack {
                Spacer()
                Button("Refresh", systemImage: "arrow.clockwise") { workspace.loadStorage() }
                    .disabled(!workspace.canEdit)
                Button("Clean up unused & old…", systemImage: "trash") { workspace.prepareDeletion(kind: .unused) }
                    .disabled(!workspace.canEdit || workspace.storageInventory == nil)
                Button("Purge all generated results…", role: .destructive) {
                    workspace.prepareDeletion(kind: .allGenerated)
                }
                .disabled(!workspace.canEdit || workspace.storageInventory == nil)
            }
            Text(
                "Clean up unused & old keeps the latest run and completed results shown in the workspace. Purge all removes every generated result. Deleting an individual artifact also invalidates results that depend on it."
            )
            .font(.callout).foregroundStyle(.secondary)
            if !workspace.canEdit {
                Text("Storage cleanup is available after processing stops.").font(.callout).foregroundStyle(.secondary)
            }
            TextField("Find by stage, artifact ID or run ID", text: $query)
                .textFieldStyle(.roundedBorder)
            Table(items, selection: $selected) {
                TableColumn("Stage") { Text($0.stage) }.width(min: 110, ideal: 140)
                TableColumn("Artifact") {
                    Text(URL(fileURLWithPath: $0.path).lastPathComponent.prefix(16)).monospaced()
                }
                TableColumn("Size") { Text(ByteCountFormatter.string(fromByteCount: $0.bytes, countStyle: .file)) }
                    .width(min: 75, ideal: 95)
                TableColumn("Files") { Text($0.fileCount.formatted()) }.width(50)
                TableColumn("Saved runs") { item in
                    Text(item.runs.isEmpty ? "Unused" : item.runs.map { String($0.prefix(8)) }.joined(separator: ", "))
                        .lineLimit(1).help(item.runs.joined(separator: "\n"))
                }
            }
            if let item = items.first(where: { $0.id == selected }) {
                HStack {
                    Text(item.path).font(.caption).textSelection(.enabled).lineLimit(1)
                    Spacer()
                    Button("Show in Finder", systemImage: "folder") {
                        NSWorkspace.shared.selectFile(
                            URL(fileURLWithPath: workspace.root).appendingPathComponent(item.path).path,
                            inFileViewerRootedAtPath: "")
                    }
                    Button("Delete artifact…", role: .destructive) {
                        workspace.prepareDeletion(kind: .artifact, artifact: item.path)
                    }
                    .disabled(!workspace.canEdit)
                }
            }
            if workspace.busy { ProgressView(workspace.status).controlSize(.small) }
        }.padding(20)
            .task { workspace.loadStorage() }
    }
}
