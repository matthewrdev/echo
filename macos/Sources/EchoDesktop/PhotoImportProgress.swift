import Foundation

struct PhotoImportProgress: Decodable, Equatable {
    let type: String
    let count: Int
    let completed: Int
    let total: Int
    let skipped: Int
    let name: String?

    var isImportEvent: Bool {
        let knownEvent = ["imageImportStarted", "imageImported", "imageImportSkipped"].contains(type)
        let validRange = total > 0 && (0...total).contains(completed)
        let validCounts = count >= 0 && skipped >= 0 && count + skipped == completed
        return knownEvent && validRange && validCounts
    }

    var fractionCompleted: Double { total > 0 ? Double(completed) / Double(total) : 0 }

    var summary: String {
        if skipped > 0 {
            return
                "Checked \(completed.formatted())/\(total.formatted()) · \(count.formatted()) imported · \(skipped.formatted()) already present"
        }
        return "Imported \(completed.formatted())/\(total.formatted())"
    }

    var detail: String {
        guard let name else { return summary }
        return "\(summary) · \(name)"
    }
}
