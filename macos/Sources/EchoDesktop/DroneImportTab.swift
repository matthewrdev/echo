import Foundation

enum DroneImportTab: String, CaseIterable, Identifiable {
    case photos = "Photos"
    case groups = "Groups"
    case map = "Map"

    var id: Self { self }
}
