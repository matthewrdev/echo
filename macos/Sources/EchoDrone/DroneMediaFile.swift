import Foundation

package struct DroneMediaFile: Identifiable, Hashable, Sendable {
    package let url: URL
    package let byteCount: Int
    package let modifiedAt: Date
    package let fileNumber: UInt64

    package var id: URL { url }

    package init?(url: URL, attributes: [FileAttributeKey: Any]) {
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
