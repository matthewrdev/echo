import SwiftUI

struct MatchPatchView: View {
    let title: String
    let image: NSImage
    let photo: Photo
    let pixel: [Double]
    @StateObject private var controller = CanvasViewportController()

    private var patch: ImagePatch? {
        guard let source = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        let x = pixel[0] * Double(source.width) / Double(photo.width)
        let y = pixel[1] * Double(source.height) / Double(photo.height)
        let radius = 64.0 * Double(source.width) / Double(photo.width)
        let rect = CGRect(x: x - radius, y: y - radius, width: radius * 2, height: radius * 2).integral
            .intersection(CGRect(x: 0, y: 0, width: source.width, height: source.height))
        guard let crop = source.cropping(to: rect) else { return nil }
        return ImagePatch(
            image: NSImage(cgImage: crop, size: NSSize(width: crop.width, height: crop.height)),
            centre: CGPoint(x: x - rect.minX, y: y - rect.minY))
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
            Text("Scroll to zoom · drag to pan").font(.caption2).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity)
    }
}
