struct CropPointCounts: Equatable, Sendable {
    var included = 0
    var excluded = 0
    var isMesh = false

    var total: Int { included + excluded }
    var label: String { isMesh ? "Loaded preview vertices" : "Loaded preview points" }
}
