import Foundation
import simd

// Keep version 1 in step with Echo.Imaging.ColourGradeTransform and ColourGradePreview.
struct ColourGradeTransform: Sendable {
    let grade: ColourGrade
    private let gains: SIMD3<Double>

    init(_ grade: ColourGrade) {
        self.grade = grade
        gains = grade.gains
    }

    func apply(_ colour: SIMD3<UInt8>) -> SIMD3<UInt8> {
        guard !grade.isIdentity else { return colour }
        let red = tone(Double(colour.x) / 255, gain: gains.x)
        let green = tone(Double(colour.y) / 255, gain: gains.y)
        let blue = tone(Double(colour.z) / 255, gain: gains.z)
        let light = 0.2126 * red + 0.7152 * green + 0.0722 * blue
        let lift = 4 * light * (1 - light) * 0.25 * (grade.shadows * (1 - light) + grade.highlights * light)
        let chroma = max(red, green, blue) - min(red, green, blue)
        let gain = grade.saturation * (1 + grade.vibrance * (1 - chroma))
        return SIMD3(
            channel(light + lift + (red - light) * gain), channel(light + lift + (green - light) * gain),
            channel(light + lift + (blue - light) * gain))
    }

    private func tone(_ encoded: Double, gain: Double) -> Double {
        let linear = (encoded <= 0.04045 ? encoded / 12.92 : pow((encoded + 0.055) / 1.055, 2.4)) * gain
        let exposed = linear <= 0.0031308 ? linear * 12.92 : 1.055 * pow(linear, 1 / 2.4) - 0.055
        return min(
            1,
            max(0, ((exposed - grade.blackPoint) / (grade.whitePoint - grade.blackPoint) - 0.5) * grade.contrast + 0.5))
    }

    private func channel(_ value: Double) -> UInt8 { UInt8(floor(min(1, max(0, value)) * 255 + 0.5)) }
}
