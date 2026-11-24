import Foundation

enum DroneCleanupMode: String, Identifiable {
    case imported
    case allMedia

    var id: Self { self }
    var title: String {
        switch self {
        case .imported: "Delete Imported Photos"
        case .allMedia: "Clear Camera Media"
        }
    }
}
