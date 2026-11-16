import Foundation

struct RunProgress: Decodable {
    var operation: String? = nil
    var unit: String? = nil
    var operationStarted: String? = nil
    var resources: ResourceProgress? = nil
    var phase: String? = nil
    let stage: String
    let message: String
    let completed: Int?
    let total: Int?
    let updated: String?

    var fractionCompleted: Double? {
        guard let completed, let total, total > 0 else { return nil }
        return min(1, max(0, Double(completed) / Double(total)))
    }

    var activityDescription: String {
        var lines: [String] = []
        if let operation, !operation.isEmpty { lines.append(operation) }
        if !message.isEmpty { lines.append(message) }
        if let completed, let total, total > 0 {
            lines.append("\(completed.formatted()) of \(total.formatted()) \(unit ?? "items")")
        }
        if let resources { lines.append(resources.summary) }
        return lines.joined(separator: "\n")
    }

    var countSummary: String? {
        guard let completed, let total, let fractionCompleted else { return nil }
        return "\(completed.formatted()) of \(total.formatted()) \(unit ?? "items") · \(Int(fractionCompleted * 100))%"
    }

    var updatedAt: Date? {
        guard let updated else { return nil }
        let parser = ISO8601DateFormatter()
        parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = parser.date(from: updated) { return date }
        parser.formatOptions = [.withInternetDateTime]
        return parser.date(from: updated)
    }
}
