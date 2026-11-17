import SceneKit
import SwiftUI

struct SourceCameraComparison: View {
    let data: CameraInspectionData
    let imageID: String
    let scene: SCNScene
    let move: (Int) -> Void
    let cameraKey: (NSEvent) -> Bool
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
                if let index = data.cameraOrder.firstIndex(of: imageID) {
                    HStack {
                        Button("Previous camera", systemImage: "chevron.left") { move(-1) }
                            .disabled(index == 0).keyboardShortcut(.leftArrow, modifiers: [])
                        Text("Camera \(index + 1) of \(data.cameraOrder.count)").monospacedDigit()
                        Button("Next camera", systemImage: "chevron.right") { move(1) }
                            .disabled(index == data.cameraOrder.count - 1).keyboardShortcut(.rightArrow, modifiers: [])
                        Spacer()
                        Text("← / → Browse · Esc Clear").foregroundStyle(.secondary)
                    }.labelStyle(.iconOnly).buttonStyle(.plain)
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
                        Text("Virtual camera").foregroundStyle(.secondary)
                        SourceCameraViewport(scene: scene, frustum: frustum, cameraKey: cameraKey)
                            .aspectRatio(CGFloat(photo.width) / CGFloat(photo.height), contentMode: .fit)
                    }.frame(maxWidth: .infinity)
                }
                Text("Same camera position, orientation and field of view as the source photo")
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
