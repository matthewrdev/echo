import SwiftUI

struct PhotoInspector: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var image: NSImage?
    @State private var metadata: [String: JSONValue] = [:]
    @State private var query = ""
    @State private var loading = false
    @State private var metadataNotice = ""

    private var groups: [MetadataGroup] {
        let entries = metadata.map { MetadataEntry(key: $0.key, value: $0.value.description) }.filter {
            query.isEmpty || $0.key.localizedCaseInsensitiveContains(query)
                || $0.value.localizedCaseInsensitiveContains(query)
        }
        let grouped = Dictionary(grouping: entries, by: \.group)
        let priority = ["XMP-drone-dji", "GPS", "ExifIFD", "Composite", "IFD0", "DJI"]
        return grouped.keys.sorted {
            let first = priority.firstIndex(of: $0) ?? priority.count
            let second = priority.firstIndex(of: $1) ?? priority.count
            return first == second ? $0 < $1 : first < second
        }.map { name in
            MetadataGroup(
                name: name,
                entries: (grouped[name] ?? []).sorted {
                    $0.isOpaque == $1.isOpaque ? $0.name < $1.name : !$0.isOpaque
                })
        }
    }

    var body: some View {
        if let photo = workspace.selectedPhoto {
            HSplitView {
                VStack(alignment: .leading, spacing: 12) {
                    Text(photo.name).font(.headline).textSelection(.enabled)
                    Text("\(photo.camera) · \(photo.width) × \(photo.height)").foregroundStyle(.secondary)
                    if let image {
                        ImageViewport(image: image)
                    } else {
                        ProgressView("Opening photograph…").frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }.padding(18).frame(minWidth: 420, maxWidth: .infinity, maxHeight: .infinity)
                VStack(alignment: .leading, spacing: 12) {
                    HStack {
                        Text("Photo metadata").font(.headline)
                        Spacer()
                        Button("Copy all", systemImage: "doc.on.doc") {
                            workspace.copyText(
                                metadata.keys.sorted().map { "\($0): \(metadata[$0]?.description ?? "")" }.joined(
                                    separator: "\n"))
                        }.controlSize(.small)
                    }
                    TextField("Search tags or values", text: $query).textFieldStyle(.roundedBorder)
                    if loading { ProgressView("Reading original EXIF, XMP and camera tags…").controlSize(.small) }
                    Text(
                        metadataNotice.isEmpty
                            ? "\(groups.reduce(0) { $0 + $1.entries.count }) of \(metadata.count) tags · original file"
                            : metadataNotice
                    )
                    .font(.caption).foregroundStyle(.secondary)
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 16) {
                            if query.isEmpty {
                                PhotoLocationView(photo: photo, copy: workspace.copyText) {
                                    workspace.openMap(for: photo)
                                }
                            }
                            ForEach(groups) { group in
                                VStack(alignment: .leading, spacing: 12) {
                                    Text("\(group.title) · \(group.entries.count)").font(.headline)
                                    ForEach(group.entries) { entry in
                                        MetadataValueRow(entry: entry, copy: workspace.copyText)
                                        Divider()
                                    }
                                }
                            }
                        }.frame(maxWidth: .infinity, alignment: .leading)
                    }.frame(maxHeight: .infinity)
                }.padding(18).frame(minWidth: 370, idealWidth: 440, maxWidth: 520, maxHeight: .infinity)
            }
            .task(id: photo.id) {
                image = nil
                metadata = photo.metadata
                loading = true
                metadataNotice = "Stored import metadata; reading complete original tags…"
                let path = URL(fileURLWithPath: workspace.root).appendingPathComponent(photo.source)
                image = await Task.detached(priority: .userInitiated) { NSImage(contentsOf: path) }.value
                do {
                    metadata = try await workspace.metadata(for: photo)
                    metadataNotice = ""
                } catch {
                    metadataNotice = "Showing stored import metadata. \(error.localizedDescription)"
                }
                loading = false
            }
        } else {
            ContentUnavailableView("Select a photograph", systemImage: "photo")
        }
    }
}
