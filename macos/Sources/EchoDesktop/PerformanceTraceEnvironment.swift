struct PerformanceTraceEnvironment: Codable {
    let runtime: String
    let operatingSystem: String
    let architecture: String
    let logicalProcessors: Int
    let serverGc: Bool
}
