import Foundation

struct DroneImportProject: Identifiable, Sendable {
    let id: UUID
    let name: String
    let url: URL
    let receipts: [DroneImportReceipt]
}
