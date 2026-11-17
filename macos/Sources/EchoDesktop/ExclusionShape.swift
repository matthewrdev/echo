enum ExclusionShape: String, Codable, CaseIterable, Sendable {
    case box, cylinder

    var title: String { self == .box ? "Box" : "Cylinder" }
}
