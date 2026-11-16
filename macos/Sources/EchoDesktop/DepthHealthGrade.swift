enum DepthHealthGrade: String {
    case empty = "Empty"
    case poor = "Poor"
    case limited = "Limited"
    case partial = "Partial"
    case broad = "Broad"
    case strong = "Strong"
    case dense = "Dense"

    init(coverage: Double) {
        switch coverage {
        case ...0: self = .empty
        case ..<0.05: self = .poor
        case ..<0.25: self = .limited
        case ..<0.5: self = .partial
        case ..<0.65: self = .broad
        case ..<0.8: self = .strong
        default: self = .dense
        }
    }
}
