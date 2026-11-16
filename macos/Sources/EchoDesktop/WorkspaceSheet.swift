import Foundation

enum WorkspaceSheet: String, Identifiable {
    case photo, settings, georeference, hints, history, storage, performance
    var id: String { rawValue }
    var title: String {
        switch self {
        case .photo: return "Photograph & metadata"
        case .settings: return "Project settings"
        case .georeference: return "Georeference"
        case .hints: return "Placement hints"
        case .storage: return "Generated files & storage"
        case .history: return "Saved runs"
        case .performance: return "Performance trace"
        }
    }
}
