enum CropRegionMode: String, Codable, CaseIterable, Sendable {
    case exclude, include

    var title: String { self == .include ? "Include" : "Exclude" }
}
