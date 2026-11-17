import Foundation

enum PipelineGroup: String, CaseIterable, Identifiable {
    case images, pointCloud, meshing, texturing, export

    var id: String { rawValue }

    var title: String {
        switch self {
        case .images: return "Images"
        case .pointCloud: return "Point cloud"
        case .meshing: return "Meshing"
        case .texturing: return "Texturing"
        case .export: return "Export"
        }
    }

    var symbol: String {
        switch self {
        case .images: return "photo.on.rectangle.angled"
        case .pointCloud: return "cloud"
        case .meshing: return "pyramid"
        case .texturing: return "square.grid.3x3"
        case .export: return "shippingbox"
        }
    }

    var stages: [PipelineStage] {
        switch self {
        case .images: return [.features, .matching, .alignment]
        case .pointCloud: return [.depth, .cloud]
        case .meshing: return [.fusion, .mesh]
        case .texturing: return [.texture]
        case .export: return [.export]
        }
    }

    var target: PipelineStage { stages.last! }
    var includesPhotographs: Bool { self == .images }
    var isExpandable: Bool { stages.count > 1 || includesPhotographs }

    var runHelp: String {
        let steps = stages.map(\.shortTitle).joined(separator: " → ")
        return "Run \(steps). Valid saved results are reused; missing or outdated prerequisites run automatically."
    }

    func contains(_ destination: WorkspaceDestination) -> Bool {
        switch destination {
        case .images: return includesPhotographs
        case .artifact(let stage): return stages.contains(stage)
        }
    }
}
