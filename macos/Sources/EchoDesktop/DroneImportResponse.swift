import Foundation

struct DroneImportResponse: Decodable {
    let imported: [DroneImportedOriginal]
}
