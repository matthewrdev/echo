enum ExclusionShape: String, Codable, CaseIterable, Sendable {
    case box, cylinder, projectedHull

    var title: String {
        switch self {
        case .box: "Box"
        case .cylinder: "Cylinder"
        case .projectedHull: "Painted hull"
        }
    }
}
