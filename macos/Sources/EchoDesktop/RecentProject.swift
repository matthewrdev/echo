import Foundation

struct RecentProject: Codable, Identifiable {
    let path: String
    let name: String
    let lastOpened: Date
    var id: String { path }
}
