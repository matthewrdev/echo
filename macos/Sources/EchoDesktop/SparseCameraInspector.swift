import SwiftUI

struct SparseCameraInspector: View {
    let data: SparseInspectionData
    let imageID: String
    let openFeatures: (String) -> Void
    @State private var image: NSImage?

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let photo = data.photos[imageID] {
                Text(photo.name).font(.headline).textSelection(.enabled)
                Text("\((data.cameraTracks[imageID]?.count ?? 0).formatted()) observed sparse points")
                    .foregroundStyle(.secondary)
                if let image {
                    Image(nsImage: image).resizable().scaledToFit()
                }
                if let camera = data.cameras[imageID] {
                    Text(
                        camera.centre.map { $0.formatted(.number.precision(.fractionLength(3))) }.joined(
                            separator: ", ")
                    )
                    .font(.caption).textSelection(.enabled)
                }
                Button("Explore features", systemImage: "viewfinder") { openFeatures(imageID) }
            }
        }
        .task(id: imageID) {
            image = nil
            guard let photo = data.photos[imageID] else { return }
            let file = data.root.appendingPathComponent(photo.preview)
            let loaded = await Task.detached(priority: .userInitiated) { NSImage(contentsOf: file) }.value
            guard !Task.isCancelled else { return }
            image = loaded
        }
    }
}
