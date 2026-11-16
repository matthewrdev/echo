import Foundation

struct RunLogEntry: Identifiable, Equatable {
    let id: Int
    let timestamp: String
    let stage: String
    let message: String
    let time: String

    init(id: Int, timestamp: String, stage: String, message: String) {
        self.id = id
        self.timestamp = timestamp
        self.stage = stage
        self.message = message
        let date =
            (try? Date.ISO8601FormatStyle(includingFractionalSeconds: true).parse(timestamp))
            ?? (try? Date.ISO8601FormatStyle().parse(timestamp))
        time = date?.formatted(.dateTime.hour().minute().second().secondFraction(.fractional(3))) ?? ""
    }
}
