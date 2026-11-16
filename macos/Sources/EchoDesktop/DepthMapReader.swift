import Foundation

final class DepthMapReader {
    func read(_ file: URL) throws -> DepthMap {
        try Task.checkCancellation()
        return try BulkArtifactReader().read(file, metadata: Data(contentsOf: file, options: .mappedIfSafe))
    }
}
