import Foundation

final class MatchingIndexReader {
    private let gate = NSLock()
    private var path = ""
    private var snapshot = MatchingIndexSnapshot()

    func read(_ file: URL) throws -> [MatchingIndexEntry] {
        gate.lock()
        defer { gate.unlock() }
        let handle = try FileHandle(forReadingFrom: file)
        defer { try? handle.close() }
        let size = try handle.seekToEnd()
        if path != file.path || size < snapshot.offset {
            path = file.path
            snapshot = MatchingIndexSnapshot()
        }
        try handle.seek(toOffset: snapshot.offset)
        let bytes = try handle.read(upToCount: Int(size - snapshot.offset)) ?? Data()
        var cursor = BinaryJournalCursor(bytes)
        let originalOffset = snapshot.offset
        if originalOffset == 0 {
            guard bytes.count >= 4, try cursor.integer() == 0x314D_4945 else {
                throw CLIError(message: "Invalid matching index.")
            }
            snapshot.offset = 4
        }
        while let frame = try cursor.frame() {
            try Task.checkCancellation()
            var record = BinaryJournalCursor(frame)
            let entry = try MatchingIndexEntry(
                sequence: Int(record.integer()), artifact: record.string(), a: record.string(), b: record.string(),
                pointCount: Int(record.integer()), lineCount: Int(record.integer()))
            let validPath =
                entry.artifact.hasPrefix("artifacts/") && !entry.artifact.split(separator: "/").contains("..")
            guard record.offset == frame.count, entry.sequence == snapshot.entries.count, validPath else {
                throw CLIError(message: "Invalid matching index entry")
            }
            snapshot.entries.append(entry)
            snapshot.offset = originalOffset + UInt64(cursor.offset)
        }
        return snapshot.entries
    }
}
