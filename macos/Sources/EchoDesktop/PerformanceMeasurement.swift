struct PerformanceMeasurement: Codable, Identifiable {
    let stage: String
    let name: String
    let unit: String
    let count: Int64
    let total: Double
    let minimum: Double
    let maximum: Double
    var id: String { "\(stage)/\(name)/\(unit)" }
    var mean: Double { count > 0 ? total / Double(count) : 0 }
}
