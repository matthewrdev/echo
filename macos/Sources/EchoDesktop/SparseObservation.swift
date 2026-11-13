import Foundation

struct SparseObservation: Identifiable {
    let imageID: String
    let pixel: [Double]
    let error: Double?
    var id: String { imageID }
}
