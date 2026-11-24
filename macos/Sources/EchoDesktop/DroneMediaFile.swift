import Foundation

struct DroneMediaFile: Identifiable, Hashable, Sendable {
    let url: URL
    let byteCount: Int
    let modifiedAt: Date
    let fileNumber: UInt64

    var id: URL { url }

    init?(url: URL, attributes: [FileAttributeKey: Any]) {
        guard attributes[.type] as? FileAttributeType == .typeRegular,
            let byteCount = attributes[.size] as? Int,
            let modifiedAt = attributes[.modificationDate] as? Date,
            let fileNumber = attributes[.systemFileNumber] as? UInt64
        else { return nil }

        self.url = url
        self.byteCount = byteCount
        self.modifiedAt = modifiedAt
        self.fileNumber = fileNumber
    }
}
