import Foundation

// Coarse RGB groups keep the complete distribution readable. The swatch is an
// observed colour from the group, never a synthetic rainbow or channel colour.
struct ScanColourBin: Sendable, Identifiable {
    let id: Int
    var count: UInt64 = 0
    var colour = SIMD3<UInt8>(repeating: 0)
    private var representativeWeight: UInt64 = 0

    init(id: Int) {
        self.id = id
    }

    mutating func add(_ value: SIMD3<UInt8>, count amount: UInt64) {
        count += amount
        if amount > representativeWeight {
            representativeWeight = amount
            colour = value
        }
    }

    var hex: String { String(format: "#%02X%02X%02X", Int(colour.x), Int(colour.y), Int(colour.z)) }

    var sortOrder: Double {
        let red = Double(colour.x) / 255
        let green = Double(colour.y) / 255
        let blue = Double(colour.z) / 255
        let maximum = max(red, green, blue)
        let minimum = min(red, green, blue)
        let chroma = maximum - minimum
        let lightness = (maximum + minimum) / 2
        if chroma < 0.08 { return lightness }
        let hue: Double
        if maximum == red {
            hue = ((green - blue) / chroma).truncatingRemainder(dividingBy: 6)
        } else if maximum == green {
            hue = (blue - red) / chroma + 2
        } else {
            hue = (red - green) / chroma + 4
        }
        return 2 + (hue < 0 ? hue + 6 : hue) + lightness * 0.001
    }
}
