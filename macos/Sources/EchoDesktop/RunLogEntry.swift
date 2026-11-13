import Foundation

struct RunLogEntry: Identifiable {
    let id: Int
    let timestamp: String
    let stage: String
    let message: String
    var time: String {
        let parser = ISO8601DateFormatter()
        parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        guard let date = parser.date(from: timestamp) else { return "" }
        return date.formatted(date: .omitted, time: .standard)
    }
}
