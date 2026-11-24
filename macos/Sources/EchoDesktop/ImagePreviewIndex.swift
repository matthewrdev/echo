import Foundation

final class ImagePreviewIndex {
    private let gate = NSLock()
    private var file: URL?
    private var offset: UInt64 = 0
    private var units: [String: String] = [:]

    func read(_ file: URL) throws -> [String: String] {
        gate.lock()
        defer { gate.unlock() }

        let handle = try FileHandle(forReadingFrom: file)
        defer { try? handle.close() }
        let length = try handle.seekToEnd()
        if self.file != file || length < offset {
            self.file = file
            offset = 0
            units.removeAll()
        }

        try handle.seek(toOffset: offset)
        let bytes = try handle.read(upToCount: Int(length - offset)) ?? Data()
        var cursor = BinaryJournalCursor(bytes)
        let originalOffset = offset
        if originalOffset == 0 {
            guard bytes.count >= 4, try cursor.integer() == 0x3155_4945 else {
                throw CLIError(message: "Invalid image preview index.")
            }
            offset = 4
        }

        while let frame = try cursor.frame() {
            try Task.checkCancellation()
            var record = BinaryJournalCursor(frame)
            let image = try record.string()
            let path = try record.string()
            guard record.offset == frame.count, path.hasPrefix("artifacts/"),
                !path.split(separator: "/").contains("..")
            else { throw CLIError(message: "Invalid image preview entry.") }
            units[image] = path
            offset = originalOffset + UInt64(cursor.offset)
        }
        return units
    }
}
