import Foundation

struct FeatureSet: Decodable {
    let imageId: String
    let lines: [LineFeature]?
    let features: [ImageFeature]
}
