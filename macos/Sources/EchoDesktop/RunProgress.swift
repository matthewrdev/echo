import Foundation

struct RunProgress: Decodable {
    let stage: String
    let message: String
    let completed: Int?
    let total: Int?
}
