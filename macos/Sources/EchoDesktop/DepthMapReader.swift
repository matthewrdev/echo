import Foundation
import stdlib_h
import xlocale

final class DepthMapReader {
    func read(_ file: URL) throws -> DepthMap {
        try Task.checkCancellation()
        let data = try Data(contentsOf: file, options: .mappedIfSafe)
        guard let locale = newlocale(LC_NUMERIC_MASK, "C", nil) else {
            throw CLIError(message: "Cannot create the depth map numeric parser.")
        }
        defer { freelocale(locale) }
        return try data.withUnsafeBytes { buffer in
            var parser = DepthMapJSONParser(bytes: buffer.bindMemory(to: UInt8.self), locale: locale)
            return try parser.read()
        }
    }
}
