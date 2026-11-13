import Foundation

struct RunSummary: Decodable, Identifiable {
    let id: String
    let status: String
    let target: String
    let created: String
}
