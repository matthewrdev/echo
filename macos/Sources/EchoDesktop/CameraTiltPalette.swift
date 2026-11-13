import AppKit

struct CameraTiltPalette {
    func color(pitch: Double?) -> NSColor {
        guard let pitch else { return .systemGray }
        let value = min(1, max(0, (pitch + 90) / 180))
        // Blue points down, gold is horizontal, red points up. Selection retains its angle colour.
        let stops = [[0.1, 0.4, 1.0], [0.0, 0.8, 0.95], [1.0, 0.82, 0.12], [1.0, 0.4, 0.05], [0.95, 0.12, 0.12]]
        let position = value * 4
        let index = min(3, Int(position))
        let a = stops[index]
        let b = stops[index + 1]
        let t = position - Double(index)
        return NSColor(
            srgbRed: a[0] + (b[0] - a[0]) * t, green: a[1] + (b[1] - a[1]) * t,
            blue: a[2] + (b[2] - a[2]) * t, alpha: 1)
    }
}
