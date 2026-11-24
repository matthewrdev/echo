import ImageIO
import SwiftUI

struct DronePhotoThumbnail: View {
    let url: URL
    let width: CGFloat
    let height: CGFloat
    @State private var image: NSImage?

    var body: some View {
        Group {
            if let image {
                Image(nsImage: image)
                    .resizable().scaledToFit()
            } else {
                Image(systemName: "photo").foregroundStyle(.secondary)
            }
        }
        .frame(width: width, height: height)
        .background(.quaternary, in: RoundedRectangle(cornerRadius: 4))
        .task(id: url) {
            image = nil
            let pixels = Int(max(width, height) * 2)
            let thumbnail = await Task.detached(priority: .utility) {
                guard
                    let source = CGImageSourceCreateWithURL(
                        url as CFURL,
                        [
                            kCGImageSourceShouldCache: false
                        ] as CFDictionary)
                else { return nil as CGImage? }
                let options: [CFString: Any] = [
                    kCGImageSourceCreateThumbnailFromImageAlways: true,
                    kCGImageSourceCreateThumbnailWithTransform: true,
                    kCGImageSourceThumbnailMaxPixelSize: pixels,
                ]
                return CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary)
            }.value
            guard !Task.isCancelled, let thumbnail else { return }
            image = NSImage(cgImage: thumbnail, size: .zero)
        }
    }
}
