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
        let bytes = try handle.readToEnd() ?? Data()
        var start = bytes.startIndex
        while let end = bytes[start...].firstIndex(of: 10) {
            try Task.checkCancellation()
            let entry = try JSONDecoder().decode(MatchingIndexEntry.self, from: bytes[start..<end])
            guard entry.sequence == snapshot.entries.count,
                entry.artifact.hasPrefix("artifacts/"), !entry.artifact.split(separator: "/").contains("..")
            else {
                throw CLIError(message: "Invalid matching index entry")
            }
            snapshot.entries.append(entry)
            snapshot.offset += UInt64(end - start + 1)
            start = bytes.index(after: end)
        }
        // A live writer may be between writes. Keep the trailing fragment for the next read.
        return snapshot.entries
    }
}
