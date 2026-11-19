enum CropRegionMode: String, Codable, CaseIterable, Sendable {
    case include, exclude

    var title: String { self == .include ? "Include" : "Exclude" }
}
