import simd

struct TextureBrushTreeNode: Sendable {
    let minimum: SIMD3<Double>
    let maximum: SIMD3<Double>
    let start: Int
    let count: Int
    var left: Int = -1
    var right: Int = -1
}
