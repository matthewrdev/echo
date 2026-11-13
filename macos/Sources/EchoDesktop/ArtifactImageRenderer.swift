import AppKit
import ImageIO

final class ArtifactImageRenderer {
    func depth(_ map: DepthMap, confidence: Bool) throws -> NSImage {
        let values = map.depth.filter { $0 > 0 }.sorted()
        let near = values.first ?? 0
        let far = values.last ?? 1
        var bytes = [UInt8](repeating: 0, count: map.width * map.height * 4)
        for index in map.depth.indices {
            let value = confidence ? map.confidence[index] : (map.depth[index] - near) / max(0.001, far - near)
            let visible = map.depth[index] > 0
            let color = NSColor(
                calibratedHue: CGFloat((1 - min(1, max(0, value))) * 0.65), saturation: 0.8,
                brightness: visible ? 0.95 : 0.08, alpha: 1
            )
            .usingColorSpace(.deviceRGB)!
            bytes[index * 4] = UInt8(color.redComponent * 255)
            bytes[index * 4 + 1] = UInt8(color.greenComponent * 255)
            bytes[index * 4 + 2] = UInt8(color.blueComponent * 255)
            bytes[index * 4 + 3] = 255
        }
        let data = Data(bytes)
        guard let provider = CGDataProvider(data: data as CFData),
            let image = CGImage(
                width: map.width, height: map.height, bitsPerComponent: 8, bitsPerPixel: 32,
                bytesPerRow: map.width * 4, space: CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
                provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
        else {
            throw CLIError(message: "Could not render depth image.")
        }
        return NSImage(cgImage: image, size: NSSize(width: map.width, height: map.height))
    }

    func volume(_ volume: FusedVolume, axis: String, slice: Double) throws -> NSImage {
        func coordinate(_ sample: VolumeSample) -> Int { axis == "X" ? sample.x : axis == "Y" ? sample.y : sample.z }
        let minimum = volume.samples.map(coordinate).min() ?? 0
        let maximum = volume.samples.map(coordinate).max() ?? 1
        let selected = Int(Double(minimum) + Double(maximum - minimum) * slice)
        let samples = volume.samples.filter { abs(coordinate($0) - selected) <= 1 }
        let context = try canvas(width: 1000, height: 1000)
        context.setFillColor(NSColor.black.cgColor)
        context.fill(CGRect(x: 0, y: 0, width: 1000, height: 1000))
        let horizontal = samples.map { axis == "X" ? $0.z : $0.x }
        let vertical = samples.map { axis == "Y" ? $0.z : $0.y }
        let left = horizontal.min() ?? 0
        let bottom = vertical.min() ?? 0
        let span = max(1, max((horizontal.max() ?? 1) - left, (vertical.max() ?? 1) - bottom))
        let scale = 960.0 / Double(span)
        for (index, sample) in samples.enumerated() {
            context.setFillColor((sample.distance < 0 ? NSColor.systemBlue : NSColor.systemOrange).cgColor)
            context.fill(
                CGRect(
                    x: 20 + Double(horizontal[index] - left) * scale, y: 20 + Double(vertical[index] - bottom) * scale,
                    width: max(2, scale), height: max(2, scale)))
        }
        return try image(context)
    }

    private func load(_ url: URL) throws -> CGImage {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
            let image = CGImageSourceCreateImageAtIndex(source, 0, nil)
        else {
            throw CLIError(message: "Cannot open image: \(url.lastPathComponent)")
        }
        return image
    }

    private func canvas(width: Int, height: Int) throws -> CGContext {
        guard
            let context = CGContext(
                data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else {
            throw CLIError(message: "Could not allocate image preview.")
        }
        return context
    }

    private func image(_ context: CGContext) throws -> NSImage {
        guard let image = context.makeImage() else { throw CLIError(message: "Could not render artifact.") }
        return NSImage(cgImage: image, size: NSSize(width: context.width, height: context.height))
    }
}
