import Foundation

struct FeatureMatchLink: Identifiable {
    let unitID: String
    let matchIndex: Int
    let featureIndex: Int
    let otherPhotoName: String
    let otherPixel: [Double]
    let score: Double?
    let manual: Bool
    var id: String { unitID + "#" + String(matchIndex) }
}
