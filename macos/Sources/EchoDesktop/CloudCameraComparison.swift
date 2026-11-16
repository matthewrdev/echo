import SceneKit
import SwiftUI

struct CloudCameraComparison: View {
    let data: CloudInspectionData
    let imageID: String
    let scene: SCNScene
    let clear: () -> Void
    @State private var image: NSImage?
    @State private var imageLoaded = false

    var body: some View {
        if let photo = data.photos[imageID], let frustum = data.frustums[imageID] {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(photo.name).fontWeight(.semibold).lineLimit(1).truncationMode(.middle)
                    Spacer(minLength: 8)
                    Button("Clear camera selection", systemImage: "xmark", action: clear)
                        .labelStyle(.iconOnly).buttonStyle(.plain).help("Clear camera selection (Esc)")
                        .keyboardShortcut(.cancelAction)
                }
                HStack(alignment: .top, spacing: 10) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Source photo").foregroundStyle(.secondary)
                        ZStack {
                            Color.black.opacity(0.06)
                            if let image {
                                Image(nsImage: image).resizable().scaledToFit()
                                    .accessibilityLabel("Source photograph \(photo.name)")
                            } else if imageLoaded {
                                Text("Source photo unavailable").foregroundStyle(.secondary)
                            } else {
                                ProgressView().controlSize(.small)
                            }
                        }.aspectRatio(CGFloat(photo.width) / CGFloat(photo.height), contentMode: .fit)
                    }.frame(maxWidth: .infinity)
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Camera view · point cloud").foregroundStyle(.secondary)
                        CameraPointCloudView(scene: scene, frustum: frustum)
                            .aspectRatio(CGFloat(photo.width) / CGFloat(photo.height), contentMode: .fit)
                    }.frame(maxWidth: .infinity)
                }
                Text("Field of view highlighted · other points at 10% opacity")
                    .font(.caption2).foregroundStyle(.secondary)
            }
            .font(.caption).padding(10)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
            .task(id: imageID) {
                image = nil
                imageLoaded = false
                let file = data.root.appendingPathComponent(photo.source)
                let loaded = await Task.detached(priority: .userInitiated) { NSImage(contentsOf: file) }.value
                guard !Task.isCancelled else { return }
                image = loaded
                imageLoaded = true
            }
        }
    }
}
