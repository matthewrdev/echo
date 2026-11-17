import Foundation

struct PerformanceStageTiming {
    let milliseconds: Double
    let gpuMilliseconds: Double
    let usesMetal: Bool
    let attempt: PerformanceTraceSummary

    init(span: PerformanceSpan, document: PerformanceTraceDocument) {
        milliseconds = span.durationMilliseconds
        let statistics = document.statistics.filter { $0.stage == span.stage }
        gpuMilliseconds = statistics.compactMap(\.gpuMilliseconds).reduce(0, +)
        usesMetal = statistics.contains { $0.backend == .metal }
        attempt = document.summary
    }
}
