import Foundation

struct WorkspaceFileStamp: Equatable {
    let inode: UInt64?
    let modified: Date?
    let size: UInt64?

    init?(file: URL) {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: file.path) else { return nil }
        inode = (attributes[.systemFileNumber] as? NSNumber)?.uint64Value
        modified = attributes[.modificationDate] as? Date
        size = (attributes[.size] as? NSNumber)?.uint64Value
    }
}
