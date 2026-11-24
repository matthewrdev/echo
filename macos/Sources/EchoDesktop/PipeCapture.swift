import Foundation

final class PipeCapture {
    private let handle: FileHandle
    private let progress: (@Sendable (String) -> Void)?
    private(set) var data = Data()

    init(handle: FileHandle, progress: (@Sendable (String) -> Void)?) {
        self.handle = handle
        self.progress = progress
    }

    func readToEnd() {
        var pending = Data()
        while let chunk = try? handle.read(upToCount: 65_536), !chunk.isEmpty {
            data.append(chunk)
            guard let progress else { continue }
            pending.append(chunk)
            while let newline = pending.firstIndex(of: 10) {
                progress(String(decoding: pending[..<newline], as: UTF8.self))
                pending.removeSubrange(...newline)
            }
        }

        if !pending.isEmpty {
            progress?(String(decoding: pending, as: UTF8.self))
        }
    }
}
