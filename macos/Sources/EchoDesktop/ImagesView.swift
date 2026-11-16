import SwiftUI

struct ImagesView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var collapsed: Set<String> = []
    @State private var gridColumnCount = 1
    @State private var selectedTile: PhotoTileIdentifier?
    @FocusState private var gridFocused: Bool
    @AppStorage("images.tileWidth") private var tileWidth = 190.0

    private var photos: [Photo] { workspace.inspection?.project.images ?? [] }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            filters
            if workspace.imagesViewMode == .map {
                PhotoMapView(workspace: workspace)
            } else {
                tiles
            }
            if let photo = workspace.selectedPhoto {
                Divider()
                selectionDetails(photo)
            }
        }
        .padding(18).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var tiles: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text(
                    "\(workspace.filteredPhotos.count.formatted()) of \(photos.count.formatted()) images · \(photos.filter(\.enabled).count.formatted()) enabled"
                )
                .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Image(systemName: "square.grid.2x2").foregroundStyle(.secondary)
                Slider(value: $tileWidth, in: 150...300)
                    .frame(width: 100).accessibilityLabel("Image tile size").help("Image tile size")
            }
            if photos.isEmpty {
                ContentUnavailableView {
                    Label("Add images to your project", systemImage: "photo.on.rectangle.angled")
                } description: {
                    Text("Import a folder of photographs to begin reconstruction.")
                } actions: {
                    Button("Import photos") { workspace.importPhotos() }.disabled(!workspace.canEdit)
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if workspace.filteredPhotos.isEmpty {
                ContentUnavailableView {
                    Label("No matching images", systemImage: "magnifyingglass")
                } description: {
                    Text("Try another name, camera, set or filter.")
                } actions: {
                    Button("Clear filters") {
                        workspace.query = ""
                        workspace.selectedSet = "All sets"
                        workspace.locationFilter = "All images"
                    }
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                photoGrid
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var photoGrid: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 20) {
                    ForEach(workspace.photoGroups) { group in
                        DisclosureGroup(
                            isExpanded: Binding(
                                get: { !collapsed.contains(group.name) },
                                set: { expanded in
                                    if expanded {
                                        collapsed.remove(group.name)
                                    } else {
                                        collapsed.insert(group.name)
                                    }
                                })
                        ) {
                            LazyVGrid(columns: [GridItem(.adaptive(minimum: tileWidth), spacing: 14)], spacing: 14) {
                                ForEach(group.photos) { photo in
                                    let id = PhotoTileIdentifier(group: group.name, photoID: photo.id)
                                    PhotoTile(photo: photo, workspace: workspace) { selectTile(id) }
                                        .id(id)
                                }
                            }
                            .background {
                                GeometryReader { geometry in
                                    let columns = max(1, Int((geometry.size.width + 14) / (tileWidth + 14)))
                                    Color.clear.onChange(of: columns, initial: true) { _, count in
                                        gridColumnCount = count
                                    }
                                }
                            }
                            .padding(.top, 8)
                        } label: {
                            Text("\(group.name) · \(group.photos.count.formatted())")
                                .font(.subheadline.weight(.semibold))
                        }
                    }
                }.padding(2)
            }
            .focusable()
            .focused($gridFocused)
            .onKeyPress(keys: [.upArrow, .downArrow, .leftArrow, .rightArrow]) { press in
                guard press.modifiers.intersection([.command, .control, .option, .shift]).isEmpty else {
                    return .ignored
                }
                let rows = navigationRows
                let current =
                    rows.joined().first {
                        $0 == selectedTile && $0.photoID == workspace.selectedPhotoID
                    } ?? rows.joined().first { $0.photoID == workspace.selectedPhotoID }
                guard let next = SelectionNavigation().destination(in: rows, from: current, key: press.key) else {
                    return .ignored
                }
                selectTile(next)
                proxy.scrollTo(next)
                return .handled
            }
        }
    }

    private var navigationRows: [[PhotoTileIdentifier]] {
        workspace.photoGroups.filter { !collapsed.contains($0.name) }.flatMap { group in
            return stride(from: 0, to: group.photos.count, by: gridColumnCount).map { start in
                group.photos[start..<min(start + gridColumnCount, group.photos.count)].map {
                    PhotoTileIdentifier(group: group.name, photoID: $0.id)
                }
            }
        }
    }

    private func selectTile(_ id: PhotoTileIdentifier) {
        gridFocused = true
        selectedTile = id
        workspace.selectedPhotoID = id.photoID
    }

    private var filters: some View {
        HStack(spacing: 12) {
            TextField("Search name or camera", text: $workspace.query).textFieldStyle(.roundedBorder)
                .accessibilityLabel("Search images")
            Picker("Set", selection: $workspace.selectedSet) {
                ForEach(workspace.sets, id: \.self) { Text($0) }
            }.labelsHidden().frame(maxWidth: 180).help("Filter by image set")
            Picker("Filter", selection: $workspace.locationFilter) {
                ForEach(["All images", "Discarded", "GPS present", "No GPS", "Unplaced", "Disabled"], id: \.self) {
                    Text($0)
                }
            }.labelsHidden().frame(width: 140).help("Filter images")
        }
    }

    private func selectionDetails(_ photo: Photo) -> some View {
        HStack(spacing: 16) {
            VStack(alignment: .leading, spacing: 3) {
                Text(photo.name).font(.caption.weight(.semibold)).lineLimit(1).truncationMode(.middle)
                if photo.isDiscarded {
                    Text("Discarded · restore to include in processing").font(.caption2).foregroundStyle(.secondary)
                } else {
                    Text("\(photo.camera) · \(photo.width) × \(photo.height)").font(.caption2).foregroundStyle(
                        .secondary)
                }
            }
            Spacer(minLength: 0)
            Toggle(
                "Use in processing",
                isOn: Binding(get: { photo.enabled }, set: { workspace.setEnabled(photo, enabled: $0) })
            ).disabled(!workspace.canEdit)
            Button("Open photograph & metadata", systemImage: "info.circle") { workspace.sheet = .photo }
                .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).help("Open photograph & metadata")
            Button("Clear photo selection", systemImage: "xmark") { workspace.selectedPhotoID = nil }
                .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).help("Clear photo selection")
        }.controlSize(.small)
    }
}
