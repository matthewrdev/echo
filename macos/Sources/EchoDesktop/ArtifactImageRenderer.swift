import AppKit
import ImageIO

final class ArtifactImageRenderer {
    func depth(_ map: DepthMap, confidence: Bool) throws -> NSImage {
        let dimensions = map.width.multipliedReportingOverflow(by: map.height)
        let validDimensions = map.width > 0 && map.height > 0 && !dimensions.overflow
        guard validDimensions, dimensions.partialValue <= Int.max / 4,
            map.depth.count == dimensions.partialValue, map.confidence.count == map.depth.count
        else {
            throw CLIError(message: "Depth image dimensions do not match its samples.")
        }
        var near = Float.infinity
        var far = -Float.infinity
        for depth in map.depth where depth.isFinite && depth > 0 {
            near = min(near, depth)
            far = max(far, depth)
        }
        let range = max(0.001, far - near)
        var bytes = [UInt8](repeating: 0, count: map.depth.count * 4)
        for index in map.depth.indices {
            if index % 65536 == 0 { try Task.checkCancellation() }
            let depth = map.depth[index]
            guard depth.isFinite && depth > 0 else { continue }
            let value = confidence ? map.confidence[index] : (depth - near) / range
            let hue = (1 - min(1, max(0, value.isFinite ? value : 0))) * 3.9
            // HSV with fixed saturation/brightness, without per-pixel NSColor allocations.
            let chroma: Float = 0.95 * 0.8
            let secondary = chroma * (1 - abs(hue.truncatingRemainder(dividingBy: 2) - 1))
            let base: Float = 0.95 - chroma
            let red = hue < 1 ? chroma : hue < 2 ? secondary : 0
            let green = hue < 1 ? secondary : hue < 3 ? chroma : secondary
            let blue = hue < 2 ? 0 : hue < 3 ? secondary : chroma
            bytes[index * 4] = UInt8((red + base) * 255)
            bytes[index * 4 + 1] = UInt8((green + base) * 255)
            bytes[index * 4 + 2] = UInt8((blue + base) * 255)
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
