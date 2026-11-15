struct FusionGridPoint: Hashable {
    let x: Int
    let y: Int
    let z: Int

    func adding(_ other: FusionGridPoint) -> FusionGridPoint {
        FusionGridPoint(x: x + other.x, y: y + other.y, z: z + other.z)
    }

    func coordinate(_ axis: String) -> Int {
        axis == "X" ? x : axis == "Y" ? y : z
    }
}
