import Foundation

struct MetadataGroup: Identifiable {
    let name: String
    let entries: [MetadataEntry]
    var id: String { name }
    var title: String {
        switch name {
        case "XMP-drone-dji": return "DJI flight & camera"
        case "GPS": return "GPS location"
        case "ExifIFD": return "Camera & exposure"
        case "DJI": return "DJI maker notes"
        default: return name
        }
    }
}
