import simd

struct FusionSurface {
    var positions: [SIMD3<Float>] = []
    var normals: [SIMD3<Float>] = []
    var support: [Int] = []
    var indices: [UInt32] = []

    var triangleCount: Int { indices.count / 3 }
}
