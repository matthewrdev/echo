import SwiftUI

struct ProjectSidebar: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var collapsed: Set<String> = []

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 9) {
                HStack(alignment: .top, spacing: 8) {
                    Button("Back to projects", systemImage: "chevron.left") { workspace.showProjects() }
                        .labelStyle(.iconOnly).buttonStyle(.borderless).disabled(!workspace.canEdit)
                    Text(workspace.inspection?.project.name ?? "Open a project")
                        .font(.headline).lineLimit(1).truncationMode(.tail)
                        .help(workspace.inspection?.project.name ?? "")
                }
                HStack(spacing: 16) {
                    Button("Project settings", systemImage: "gearshape") { workspace.sheet = .settings }
                    Button("Georeference", systemImage: "globe") { workspace.sheet = .georeference }
                    Button("Placement hints", systemImage: "pin") { workspace.sheet = .hints }
                    Button("Saved runs", systemImage: "clock.arrow.circlepath") { workspace.sheet = .history }
                }
                .labelStyle(.iconOnly).buttonStyle(.borderless).disabled(workspace.inspection == nil)
            }
            Divider()
            HStack {
                Text("PHOTOS").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                Spacer()
                Button("Photo map", systemImage: "map") { workspace.openMap() }.help(
                    "Photo locations and camera frustums")
                Button("Import photos", systemImage: "plus") { workspace.importPhotos() }.disabled(!workspace.canEdit)
            }.labelStyle(.iconOnly).buttonStyle(.borderless)
            TextField("Search name or camera", text: $workspace.query).textFieldStyle(.roundedBorder)
            ScrollView(.horizontal) {
                HStack(spacing: 4) {
                    ForEach(["All images", "GPS present", "No GPS", "Unplaced", "Disabled"], id: \.self) { filter in
                        Button(filter) { workspace.locationFilter = filter }
                            .buttonStyle(.borderless).font(.caption).padding(5)
                            .background(
                                workspace.locationFilter == filter ? Color.accentColor.opacity(0.15) : Color.clear,
                                in: RoundedRectangle(cornerRadius: 4))
                    }
                }
            }
            Text("\(workspace.filteredPhotos.count) of \(workspace.inspection?.project.images.count ?? 0) photos")
                .font(.caption).foregroundStyle(.secondary)
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 12) {
                    ForEach(workspace.photoGroups) { group in
                        DisclosureGroup(
                            isExpanded: Binding(
                                get: { !collapsed.contains(group.name) },
                                set: { expanded in
                                    if expanded { collapsed.remove(group.name) } else { collapsed.insert(group.name) }
                                })
                        ) {
                            LazyVStack(spacing: 3) {
                                ForEach(group.photos) { PhotoThumbnailRow(photo: $0, workspace: workspace) }
                            }
                        } label: {
                            Text("\(group.name) · \(group.photos.count)").font(.subheadline.weight(.semibold))
                        }
                    }
                }
            }
            if let photo = workspace.selectedPhoto {
                Divider()
                Toggle(
                    "Use in processing",
                    isOn: Binding(get: { photo.enabled }, set: { workspace.setEnabled(photo, enabled: $0) })
                )
                .disabled(!workspace.canEdit)
                Button("Open photograph & metadata") { workspace.sheet = .photo }.font(.caption)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}
