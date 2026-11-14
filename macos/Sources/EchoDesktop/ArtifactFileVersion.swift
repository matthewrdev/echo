import Foundation

struct ArtifactFileVersion: Equatable {
    let file: URL
    let size: UInt64?
    let modified: Date?

    init(file: URL) {
        self.file = file
        let attributes = try? FileManager.default.attributesOfItem(atPath: file.path)
        size = attributes?[.size] as? UInt64
        modified = attributes?[.modificationDate] as? Date
    }
}
