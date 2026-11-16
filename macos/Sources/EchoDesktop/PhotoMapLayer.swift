enum PhotoMapLayer: String, CaseIterable, Identifiable {
    case satellite = "Satellite"
    case standard = "Standard"
    case offline = "Offline grid"
    var id: String { rawValue }

    var symbol: String {
        switch self {
        case .satellite: return "globe.americas.fill"
        case .standard: return "map"
        case .offline: return "square.grid.3x3"
        }
    }

    var help: String {
        switch self {
        case .satellite: return "Satellite imagery"
        case .standard: return "Street map"
        case .offline: return "Offline location grid"
        }
    }
}
