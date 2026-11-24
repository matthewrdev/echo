import Foundation

struct RunSummary: Decodable, Equatable, Identifiable {
    let id: String
    let status: String
    let target: String
    let created: String
}
