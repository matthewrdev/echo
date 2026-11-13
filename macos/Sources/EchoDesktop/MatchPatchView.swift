import SwiftUI

struct MatchPatchView: View {
    let title: String
    let image: NSImage
    let photo: Photo
    let pixel: [Double]
    var overlay: FeaturePatchOverlay = .point(kind: nil)
    @StateObject private var controller = CanvasViewportController()

    private var patch: ImagePatch? {
        FeaturePatchBuilder().build(
            image: image, photoSize: CGSize(width: photo.width, height: photo.height),
            pixel: CGPoint(x: pixel[0], y: pixel[1]), overlay: overlay)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title).font(.caption.weight(.semibold)).lineLimit(1).help(title)
            HStack {
                Text(
                    "x \(pixel[0].formatted(.number.precision(.fractionLength(1)))) · y \(pixel[1].formatted(.number.precision(.fractionLength(1)))) px"
                )
                .font(.caption2).foregroundStyle(.secondary).textSelection(.enabled)
                Spacer()
                Button("Fit") { controller.fit() }.controlSize(.mini)
            }
            if let patch {
                CanvasViewport(
                    renderer: PatchCanvasRenderer(identity: photo.id + pixel.description, patch: patch),
                    controller: controller)
            }
            Text(overlay.title).font(.caption2).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity)
    }
}
