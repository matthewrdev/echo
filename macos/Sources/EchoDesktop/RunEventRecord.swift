import Foundation

struct RunEventRecord: Decodable {
    let sequence: Int
    let timestamp: String
    let type: String
    let payload: [String: JSONValue]
}
