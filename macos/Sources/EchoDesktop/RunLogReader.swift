import Foundation

actor RunLogReader {
    private var cursors: [String: RunLogCursor] = [:]
    private let formatter: RunEventFormatter
    private let dates = ISO8601DateFormatter()

    init(formatter: RunEventFormatter) { self.formatter = formatter }

    func read(root: String, runID: String) throws -> [RunLogEntry] {
        let path = URL(fileURLWithPath: root).appendingPathComponent("runs/\(runID)/events.bin")
        guard FileManager.default.fileExists(atPath: path.path) else { return [] }
        var cursor = cursors[path.path] ?? RunLogCursor()
        let file = try FileHandle(forReadingFrom: path)
        defer { try? file.close() }
        let length = try file.seekToEnd()
        if length < cursor.offset { cursor = RunLogCursor() }
        try file.seek(toOffset: cursor.offset)
        let bytes = try file.read(upToCount: Int(length - cursor.offset)) ?? Data()
        var journal = BinaryJournalCursor(bytes)
        let originalOffset = cursor.offset
        if originalOffset == 0 {
            guard bytes.count >= 4 else { return cursor.entries }
            guard try journal.integer() == 0x3156_4545 else {
                throw CLIError(message: "Invalid run event journal.")
            }
            cursor.offset = 4
        }
        while let frame = try journal.frame() {
            var record = BinaryJournalCursor(frame)
            let sequence = Int(try record.wideInteger())
            let ticks = try record.wideInteger()
            _ = try record.string()
            let type = try record.string()
            let payload = try record.valueMap()
            guard record.offset == frame.count else { throw CLIError(message: "Invalid run event.") }
            let date = Date(timeIntervalSince1970: Double(ticks) / 10_000_000 - 62_135_596_800)
            let event = RunEventRecord(
                sequence: sequence, timestamp: dates.string(from: date), type: type, payload: payload)
            if let entry = formatter.entry(from: event) {
                cursor.entries.append(entry)
            }
            cursor.offset = originalOffset + UInt64(journal.offset)
        }
        cursors[path.path] = cursor
        return cursor.entries
    }
}
