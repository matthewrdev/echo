enum ExclusionShape: String, Codable, CaseIterable {
    case box, cylinder

    var title: String { self == .box ? "Box" : "Cylinder" }
}
