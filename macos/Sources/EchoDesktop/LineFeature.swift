import Foundation

struct LineFeature: Decodable {
    typealias CodingKeys = LineFeatureCodingKeys
    let start: [Double]
    let end: [Double]
    private let storedDescriptor: [Double]
    let strength: Double
    var descriptorSource: FeatureDescriptorSlice? = nil
    var descriptor: [Double] { descriptorSource?.values ?? storedDescriptor }
    var descriptorCount: Int { descriptorSource?.count ?? storedDescriptor.count }

    init(
        start: [Double], end: [Double], descriptor: [Double], strength: Double,
        descriptorSource: FeatureDescriptorSlice? = nil
    ) {
        self.start = start
        self.end = end
        storedDescriptor = descriptor
        self.strength = strength
        self.descriptorSource = descriptorSource
    }
    var midpoint: [Double] { [(start[0] + end[0]) / 2, (start[1] + end[1]) / 2] }
    var length: Double { hypot(end[0] - start[0], end[1] - start[1]) }
}
