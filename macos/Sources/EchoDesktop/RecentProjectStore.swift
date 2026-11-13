import Foundation

protocol RecentProjectStore {
    func load() -> [RecentProject]
    func record(path: String, name: String) -> [RecentProject]
    func remove(path: String) -> [RecentProject]
}
