import Foundation

struct RunProgress: Decodable {
    var phase: String? = nil
    let stage: String
    let message: String
    let completed: Int?
    let total: Int?
    let updated: String?

    var updatedAt: Date? {
        guard let updated else { return nil }
        let parser = ISO8601DateFormatter()
        parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = parser.date(from: updated) { return date }
        parser.formatOptions = [.withInternetDateTime]
        return parser.date(from: updated)
    }
}
