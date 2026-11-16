import Foundation

enum WorkspaceDestination: Equatable {
    case images
    case artifact(PipelineStage)

    var title: String {
        switch self {
        case .images: return "Images"
        case .artifact(let stage): return stage.title
        }
    }
}
