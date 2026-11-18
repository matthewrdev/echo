import Foundation

struct ImageFeature: Decodable {
    typealias CodingKeys = ImageFeatureCodingKeys
    let x: Double
    let y: Double
    var kind: String? = nil
    private let storedDescriptor: [Double]
    var descriptorSource: FeatureDescriptorSlice? = nil
    var descriptor: [Double] { descriptorSource?.values ?? storedDescriptor }
    var descriptorCount: Int { descriptorSource?.count ?? storedDescriptor.count }

    init(
        x: Double, y: Double, kind: String? = nil, descriptor: [Double], descriptorSource: FeatureDescriptorSlice? = nil
    ) {
        self.x = x
        self.y = y
        self.kind = kind
        storedDescriptor = descriptor
        self.descriptorSource = descriptorSource
    }
}
