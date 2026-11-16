enum ArtifactDeletionKind: String, Decodable {
    case artifact
    case unused
    case allGenerated

    var title: String {
        switch self {
        case .artifact: "Delete artifact and dependent indexes?"
        case .unused: "Clean up unused results and old history?"
        case .allGenerated: "Purge all generated results?"
        }
    }

    var actionTitle: String {
        switch self {
        case .artifact: "Delete artifact"
        case .unused: "Clean up"
        case .allGenerated: "Purge all results"
        }
    }
}
