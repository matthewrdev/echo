enum ArtifactDeletionSelection {
    case artifact(String)
    case unused(retaining: [String])
    case allGenerated

    var arguments: [String] {
        switch self {
        case .artifact(let path): return ["--artifact", path]
        case .allGenerated: return []
        case .unused(let runs): return ["--unused", "--keep-runs", runs.sorted().joined(separator: ",")]
        }
    }
}
