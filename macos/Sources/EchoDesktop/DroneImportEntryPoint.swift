import EchoDrone
import SwiftUI

struct DroneImportEntryPoint: View {
    let model: DroneImportViewModel
    let workspace: WorkspaceViewModel
    @State private var sources: [DroneSource] = []
    @State private var selectedSource: DroneSource?
    @State private var mountRevision = 0

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            if sources.count == 1, let source = sources.first {
                Button {
                    selectedSource = source
                } label: {
                    Label("Drone Import…", systemImage: "sdcard").frame(maxWidth: .infinity)
                }
                .controlSize(.large)
                Text(source.name).font(.caption).foregroundStyle(.secondary)
            } else if !sources.isEmpty {
                Menu {
                    ForEach(sources) { source in
                        Button(source.name) { selectedSource = source }
                    }
                } label: {
                    Label("Drone Import…", systemImage: "sdcard").frame(maxWidth: .infinity)
                }
                .controlSize(.large)
            }
        }
        .task(id: mountRevision) {
            let reader = model.reader
            let detected = await Task.detached(priority: .utility) { reader.mountedSources() }.value
            guard !Task.isCancelled else { return }
            sources = detected
        }
        .onReceive(NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.didMountNotification)) { _ in
            mountRevision += 1
        }
        .onReceive(NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.didUnmountNotification)) { _ in
            mountRevision += 1
        }
        .sheet(
            item: $selectedSource,
            onDismiss: { mountRevision += 1 },
            content: { source in
                DroneImportView(
                    model: model, source: source, fileDialogLocations: workspace.fileDialogLocations,
                    didCreate: workspace.recordDroneProject, openProject: workspace.openDroneProject)
            })
    }
}
