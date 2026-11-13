import Foundation

final class UserDefaultsRecentProjectStore: RecentProjectStore {
    private let defaults: UserDefaults
    private let key = "recentProjectsV1"

    init(defaults: UserDefaults) { self.defaults = defaults }

    func load() -> [RecentProject] {
        guard let data = defaults.data(forKey: key),
            let projects = try? JSONDecoder().decode([RecentProject].self, from: data)
        else { return [] }
        return projects.sorted { $0.lastOpened > $1.lastOpened }
    }

    func record(path: String, name: String) -> [RecentProject] {
        let canonical = URL(fileURLWithPath: path).resolvingSymlinksInPath().path
        var projects = load().filter { $0.path != canonical }
        projects.insert(RecentProject(path: canonical, name: name, lastOpened: Date()), at: 0)
        return save(Array(projects.prefix(12)))
    }

    func remove(path: String) -> [RecentProject] { save(load().filter { $0.path != path }) }

    private func save(_ projects: [RecentProject]) -> [RecentProject] {
        if let data = try? JSONEncoder().encode(projects) { defaults.set(data, forKey: key) }
        return projects
    }
}
