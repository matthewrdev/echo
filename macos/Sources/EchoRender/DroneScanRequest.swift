import EchoDrone
import Foundation

struct DroneScanRequest: Decodable {
    let source: String

    func scan() throws -> [DroneScanGroup] {
        let scan = try DroneSourceReader().scanCard(at: URL(fileURLWithPath: source, isDirectory: true))
        let grouping = DroneImportGrouping()
        let suggestions = grouping.suggest(scan.photos)
        let photosByGroup = Dictionary(grouping: scan.photos) { suggestions.assignments[$0.id] }
        var groups = suggestions.groups.map {
            DroneScanGroup(name: $0.name, photos: grouping.entries(for: photosByGroup[$0.id, default: []]))
        }
        if let unassigned = photosByGroup[nil] {
            groups.append(DroneScanGroup(name: "Unassigned", photos: grouping.entries(for: unassigned)))
        }
        return groups
    }
}
