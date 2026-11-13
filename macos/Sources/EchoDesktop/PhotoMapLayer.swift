enum PhotoMapLayer: String, CaseIterable, Identifiable {
    case satellite = "Satellite"
    case standard = "Standard"
    case offline = "Offline grid"
    var id: String { rawValue }
}
