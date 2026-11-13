import AppKit

struct FeaturePatchBuilder {
    func build(image: NSImage, photoSize: CGSize, pixel: CGPoint, overlay: FeaturePatchOverlay) -> ImagePatch? {
        guard photoSize.width > 0, photoSize.height > 0,
            let source = image.cgImage(forProposedRect: nil, context: nil, hints: nil)
        else { return nil }
        let scaleX = Double(source.width) / photoSize.width
        let scaleY = Double(source.height) / photoSize.height
        var radius = 64.0
        if case .line(let start, let end) = overlay {
            radius = max(radius, max(abs(end.x - start.x), abs(end.y - start.y)) * 0.65)
        }
        let centre = CGPoint(x: pixel.x * scaleX, y: pixel.y * scaleY)
        let rect = CGRect(
            x: centre.x - radius * scaleX, y: centre.y - radius * scaleY,
            width: radius * scaleX * 2, height: radius * scaleY * 2
        ).integral.intersection(CGRect(x: 0, y: 0, width: source.width, height: source.height))
        guard !rect.isNull, let crop = source.cropping(to: rect) else { return nil }
        return ImagePatch(
            image: NSImage(cgImage: crop, size: NSSize(width: crop.width, height: crop.height)),
            centre: CGPoint(x: centre.x - rect.minX, y: centre.y - rect.minY),
            overlay: overlay.transformed(scaleX: scaleX, scaleY: scaleY, origin: rect.origin))
    }
}
