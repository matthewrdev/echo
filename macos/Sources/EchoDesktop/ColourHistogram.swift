import Foundation

struct ColourHistogram: Sendable {
    var scanColours = (0..<64).map { ScanColourBin(id: $0) }
    var red = [UInt64](repeating: 0, count: 256)
    var green = [UInt64](repeating: 0, count: 256)
    var blue = [UInt64](repeating: 0, count: 256)
    var brightness = [UInt64](repeating: 0, count: 256)
    var count: UInt64 = 0
    var clippedBlack: UInt64 = 0
    var clippedWhite: UInt64 = 0

    mutating func add(_ colour: SIMD3<UInt8>, count amount: UInt64) {
        let group = (Int(colour.x) >> 6) << 4 | (Int(colour.y) >> 6) << 2 | (Int(colour.z) >> 6)
        scanColours[group].add(colour, count: amount)
        red[Int(colour.x)] += amount
        green[Int(colour.y)] += amount
        blue[Int(colour.z)] += amount
        let redLight = 0.2126 * Double(colour.x)
        let greenLight = 0.7152 * Double(colour.y)
        let blueLight = 0.0722 * Double(colour.z)
        let luminance = Int((redLight + greenLight + blueLight).rounded())
        brightness[luminance] += amount
        count += amount
        if min(colour.x, colour.y, colour.z) == 0 { clippedBlack += amount }
        if max(colour.x, colour.y, colour.z) == 255 { clippedWhite += amount }
    }
}
