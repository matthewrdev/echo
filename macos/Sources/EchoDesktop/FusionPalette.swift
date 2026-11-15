import AppKit
import simd

struct FusionPalette {
    func support(_ views: Int) -> SIMD4<Float> {
        let low = SIMD4<Float>(0.96, 0.62, 0.20, 1)
        let middle = SIMD4<Float>(0.22, 0.80, 0.72, 1)
        let high = SIMD4<Float>(0.27, 0.55, 0.98, 1)
        if views <= 8 {
            return simd_mix(low, middle, SIMD4<Float>(repeating: Float(max(2, views) - 2) / 6))
        }
        return simd_mix(middle, high, SIMD4<Float>(repeating: Float(min(16, views) - 8) / 8))
    }

    func distance(_ distance: Float, voxelSize: Double) -> NSColor {
        let strength = min(1, abs(Double(distance)) / (voxelSize * 3))
        let surface = NSColor(calibratedWhite: 0.55, alpha: 1)
        let side = distance < 0 ? NSColor.systemBlue : NSColor.systemOrange
        return surface.blended(withFraction: strength, of: side) ?? side
    }
}
