import Foundation

struct DroneImportReceipt: Sendable {
    let file: DroneMediaFile
    let original: URL
    let hash: String
}
