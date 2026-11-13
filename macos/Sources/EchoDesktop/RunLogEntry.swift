import Foundation

struct RunLogEntry: Identifiable {
    let id: Int
    let timestamp: String
    let stage: String
    let message: String
    var time: String {
        let parser = ISO8601DateFormatter()
        parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        var date = parser.date(from: timestamp)
        if date == nil {
            parser.formatOptions = [.withInternetDateTime]
            date = parser.date(from: timestamp)
        }
        guard let date else { return "" }
        let formatter = DateFormatter()
        formatter.setLocalizedDateFormatFromTemplate("jmmssSSS")
        return formatter.string(from: date)
    }
}
