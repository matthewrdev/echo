import AppKit

struct FusionSliceRenderer {
    func render(volume: FusedVolume, bounds: FusionVolumeBounds, axis: String, fraction: Double) throws -> FusionSlice {
        let selected = bounds.sliceIndex(axis: axis, fraction: fraction)
        let horizontal = axis == "X" ? "Z" : "X"
        let vertical = axis == "Y" ? "Z" : "Y"
        let left = bounds.minimum.coordinate(horizontal)
        let bottom = bounds.minimum.coordinate(vertical)
        let columns = bounds.maximum.coordinate(horizontal) - left + 1
        let rows = bounds.maximum.coordinate(vertical) - bottom + 1
        let scale = min(960 / Double(columns), 760 / Double(rows))
        let offsetX = (1000 - Double(columns) * scale) / 2
        let offsetY = (800 - Double(rows) * scale) / 2
        guard
            let context = CGContext(
                data: nil, width: 1000, height: 800, bitsPerComponent: 8, bytesPerRow: 4000,
                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { throw CLIError(message: "Could not render the fusion slice.") }
        context.clear(CGRect(x: 0, y: 0, width: 1000, height: 800))
        var count = 0
        for (index, sample) in volume.samples.enumerated() {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let key = FusionGridPoint(x: sample.x, y: sample.y, z: sample.z)
            guard key.coordinate(axis) == selected else { continue }
            context.setFillColor(FusionPalette().distance(sample.distance, voxelSize: volume.voxelSize).cgColor)
            context.fill(
                CGRect(
                    x: offsetX + Double(key.coordinate(horizontal) - left) * scale,
                    y: offsetY + Double(key.coordinate(vertical) - bottom) * scale, width: max(1, scale),
                    height: max(1, scale)))
            count += 1
        }
        guard let image = context.makeImage() else {
            throw CLIError(message: "Could not create the fusion slice image.")
        }
        return FusionSlice(
            image: NSImage(cgImage: image, size: NSSize(width: 1000, height: 800)), index: selected, count: count)
    }
}
