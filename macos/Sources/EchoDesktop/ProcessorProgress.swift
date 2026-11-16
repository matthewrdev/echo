import Foundation

struct ProcessorProgress: Decodable {
    let cpuPercent: Double?
    let logicalProcessors: Int
    let threads: [ThreadProgress]?

    var capacityPercent: Double? {
        guard let cpuPercent, cpuPercent.isFinite, logicalProcessors > 0 else { return nil }
        return min(100, max(0, cpuPercent / Double(logicalProcessors)))
    }
}
