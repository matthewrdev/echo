import Foundation

enum WorkspaceSheet: String, Identifiable {
    case photo, map, settings, georeference, hints, history
    var id: String { rawValue }
    var title: String {
        switch self {
        case .photo: return "Photograph & metadata"
        case .map: return "Photo locations & camera frustums"
        case .settings: return "Project settings"
        case .georeference: return "Georeference"
        case .hints: return "Placement hints"
        case .history: return "Saved runs"
        }
    }
}
