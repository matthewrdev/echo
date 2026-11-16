import Foundation

struct ThreadProgress: Decodable, Identifiable {
    let processId: Int
    let threadId: UInt64
    let name: String
    let cpuPercent: Double

    var id: String { "\(processId):\(threadId)" }
    var label: String { name.isEmpty ? "Thread \(threadId)" : name }
}
