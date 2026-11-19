import Foundation
import simd

struct ColourGrade: Codable, Equatable, Sendable {
    var version = 1
    var exposure = 0.0
    var temperature = 0.0
    var tint = 0.0
    var blackPoint = 0.0
    var whitePoint = 1.0
    var contrast = 1.0
    var shadows = 0.0
    var highlights = 0.0
    var saturation = 1.0
    var vibrance = 0.0

    var isIdentity: Bool { self == ColourGrade() }
    var savedValue: ColourGrade? { isIdentity ? nil : self }
    var isValid: Bool {
        version == 1 && (-4...4).contains(exposure) && (-1...1).contains(temperature)
            && (-1...1).contains(tint) && (0...0.49).contains(blackPoint)
            && (0.51...1).contains(whitePoint) && (0.25...2).contains(contrast)
            && (-1...1).contains(shadows) && (-1...1).contains(highlights)
            && (0...2).contains(saturation) && (-1...1).contains(vibrance)
    }

    var gains: SIMD3<Double> {
        SIMD3(
            pow(2, exposure + temperature * 0.5 + tint * 0.25), pow(2, exposure - tint * 0.25),
            pow(2, exposure - temperature * 0.5 + tint * 0.25))
    }
}
