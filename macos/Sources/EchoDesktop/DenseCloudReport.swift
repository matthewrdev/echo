import Foundation

struct DenseCloudReport: Decodable {
    let inputSamples: Int
    let retainedSamples: Int
    let preview: [CloudPoint]
}
