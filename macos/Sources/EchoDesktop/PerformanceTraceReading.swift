protocol PerformanceTraceReading {
    func list(root: String) async throws -> [PerformanceTraceSummary]
    func document(root: String, run: String, attempt: String) async throws -> PerformanceTraceDocument
}
