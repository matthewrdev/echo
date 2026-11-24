import EchoDrone

struct DroneScanGroup: Encodable {
    let name: String
    let photos: [DroneImportEntry]
}
