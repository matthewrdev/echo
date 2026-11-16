import Foundation

actor RunLogReader {
    private var cursors: [String: RunLogCursor] = [:]
    private let formatter: RunEventFormatter

    init(formatter: RunEventFormatter) { self.formatter = formatter }

    func read(root: String, runID: String) throws -> [RunLogEntry] {
        let path = URL(fileURLWithPath: root).appendingPathComponent("runs/\(runID)/events.ndjson")
        guard FileManager.default.fileExists(atPath: path.path) else { return [] }
        var cursor = cursors[path.path] ?? RunLogCursor()
        let file = try FileHandle(forReadingFrom: path)
        defer { try? file.close() }
        let length = try file.seekToEnd()
        if length < cursor.offset { cursor = RunLogCursor() }
        try file.seek(toOffset: cursor.offset)
        cursor.pending.append(try file.readToEnd() ?? Data())
        cursor.offset = try file.offset()
        var consumed = cursor.pending.startIndex
        while let newline = cursor.pending[consumed...].firstIndex(of: 10) {
            let line = cursor.pending[consumed..<newline]
            if let entry = formatter.entry(from: Data(line)) {
                cursor.entries.append(entry)
            }
            consumed = cursor.pending.index(after: newline)
        }
        cursor.pending.removeSubrange(..<consumed)
        cursors[path.path] = cursor
        return cursor.entries
    }
}
