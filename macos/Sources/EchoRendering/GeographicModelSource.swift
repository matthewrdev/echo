import Foundation

package struct GeographicModelSource: Decodable {
    let path: String
    let coordinates: String?
    let atlas: String?
    package let origin: GeoPosition
}
