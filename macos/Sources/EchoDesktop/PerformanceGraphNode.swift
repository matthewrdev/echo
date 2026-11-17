import Foundation

struct PerformanceGraphNode: Identifiable {
    let id: String
    let stage: PipelineStage?
    let title: String
    let symbol: String
    let status: PerformanceStatus
    let milliseconds: Double?
    let detail: String
    let work: String
    var timingHelp: String = ""

    var durationLabel: String {
        guard let milliseconds else { return "Not recorded" }
        let duration = PerformanceDuration.format(milliseconds)
        return status == .reused ? "\(duration) original" : duration
    }

    static func make(
        _ document: PerformanceTraceDocument, originalTimings: [String: PerformanceStageTiming] = [:]
    ) -> [PerformanceGraphNode] {
        let stages = document.spans.filter { $0.kind == .stage }.sorted { $0.startMilliseconds < $1.startMilliseconds }
        let target = PipelineStage(rawValue: document.summary.target)?.shortTitle ?? document.summary.target
        var nodes = [
            PerformanceGraphNode(
                id: "preparation", stage: nil, title: "Prepare run", symbol: "checklist",
                status: stages.isEmpty ? document.summary.status : .completed,
                milliseconds: stages.first?.startMilliseconds ?? document.summary.durationMilliseconds,
                detail: "Check source photographs", work: "Requested output: \(target)")
        ]
        for span in stages {
            let stage = PipelineStage(rawValue: span.stage)
            let statistics = document.statistics.filter { $0.stage == span.stage }
            let checkpoints = statistics.filter { $0.kind == .checkpoint }
            let reused = checkpoints.reduce(0) { $0 + $1.reusedCount }
            let failed = checkpoints.reduce(0) { $0 + $1.failedCount }
            let total = checkpoints.reduce(0) { $0 + $1.count }
            let built = max(0, total - reused - failed)
            let reusedStage = document.isReused(span)
            let original = reusedStage ? originalTimings[span.stage] : nil
            let gpu = original?.gpuMilliseconds ?? statistics.compactMap(\.gpuMilliseconds).reduce(0, +)
            let metal = original?.usesMetal ?? statistics.contains { $0.backend == .metal }
            let work: String
            if reusedStage && original == nil {
                work = "Original processing time unavailable"
            } else {
                work = metal ? "Metal work · \(PerformanceDuration.format(gpu)) GPU" : "CPU / host work"
            }
            var timingHelp = "This attempt: \(PerformanceDuration.format(span.durationMilliseconds))"
            if let original { timingHelp += " · Originally processed: \(original.attempt.label)" }
            var detail = "\(built.formatted()) built · \(reused.formatted()) reused"
            if failed > 0 { detail += " · \(failed.formatted()) unfinished" }
            if total == 0 { detail = span.status == .running ? "Processing…" : "No completed checkpoints" }
            nodes.append(
                PerformanceGraphNode(
                    id: "stage-\(span.id)", stage: stage, title: stage?.title ?? span.name,
                    symbol: stage?.symbol ?? "gearshape", status: reusedStage ? .reused : span.status,
                    milliseconds: reusedStage ? original?.milliseconds : span.durationMilliseconds,
                    detail: detail, work: work, timingHelp: timingHelp))
        }
        let status = document.summary.status
        let title: String
        switch status {
        case .running: title = "Still processing"
        case .completed, .reused: title = "Requested output ready"
        case .cancelled: title = "Stopped by request"
        case .failed: title = "Processing failed"
        case .interrupted: title = "Processing interrupted"
        }
        nodes.append(
            PerformanceGraphNode(
                id: "outcome", stage: nil, title: title,
                symbol: status == .completed ? "checkmark.circle" : "clock",
                status: status, milliseconds: document.summary.durationMilliseconds,
                detail: "\(stages.count) \(stages.count == 1 ? "stage" : "stages") entered · \(target) requested",
                work: "Total CPU time: \(PerformanceDuration.format(document.summary.cpuMilliseconds))"))
        return nodes
    }
}
