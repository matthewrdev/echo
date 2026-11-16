enum ImagesViewMode: String, CaseIterable, Identifiable {
    case tiles = "Tiles"
    case map = "Map"

    var id: String { rawValue }
    var symbol: String { self == .tiles ? "square.grid.2x2" : "map" }
    var help: String { self == .tiles ? "Browse image tiles" : "View photo locations and camera frustums" }
}
