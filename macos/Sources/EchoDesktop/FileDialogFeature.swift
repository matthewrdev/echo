import Foundation

enum FileDialogFeature: String {
    case newProject
    case openProject
    case importPhotos
    case droneImportDestination
    case meshExport
    case imageExport
    case pointCloudExport
    case geographicExport
    case performanceTraceExport

    func directory(for selection: URL) -> URL {
        switch self {
        case .importPhotos, .droneImportDestination:
            return selection
        case .newProject, .openProject, .meshExport, .imageExport, .pointCloudExport, .geographicExport,
            .performanceTraceExport:
            return selection.deletingLastPathComponent()
        }
    }
}
