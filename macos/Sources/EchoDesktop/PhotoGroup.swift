import Foundation

struct PhotoGroup: Identifiable {
    let name: String
    let photos: [Photo]
    var id: String { name }
}
