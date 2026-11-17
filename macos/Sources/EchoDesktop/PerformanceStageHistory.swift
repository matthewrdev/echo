import Foundation

struct PerformanceStageHistory {
    let client: ProjectClient

    func timings(
        for document: PerformanceTraceDocument, attempts: [PerformanceTraceSummary], root: String
    ) async -> [String: PerformanceStageTiming] {
        var pending = document.reusedStages
        guard !pending.isEmpty,
            let current = try? await client.run(root: root, id: document.summary.runId),
            let startedAt = document.summary.startDate
        else { return [:] }

        var result: [String: PerformanceStageTiming] = [:]
        var artifacts = [current.id: current.artifacts]
        let earlier = attempts.filter {
            $0.id != document.summary.id && ($0.startDate.map { $0 < startedAt } ?? false)
        }.sorted { ($0.startDate ?? .distantPast) > ($1.startDate ?? .distantPast) }
        for attempt in earlier {
            guard !pending.isEmpty, !Task.isCancelled else { break }
            if artifacts[attempt.runId] == nil {
                guard let run = try? await client.run(root: root, id: attempt.runId) else { continue }
                artifacts[attempt.runId] = run.artifacts
            }
            // Stage names can recur with different inputs or reset revisions; match the saved output.
            let matching = pending.filter { stage in
                guard let artifact = current.artifacts[stage] else { return false }
                return artifacts[attempt.runId]?[stage] == artifact
            }
            guard !matching.isEmpty,
                let previous = try? await client.performanceTrace(root: root, run: attempt.runId, attempt: attempt.id),
                previous.schemaVersion == 1
            else { continue }

            for span in previous.spans where span.kind == .stage && span.status == .completed {
                guard matching.contains(span.stage), !previous.isReused(span) else { continue }
                let built = previous.statistics.contains {
                    let checkpointForStage = $0.stage == span.stage && $0.kind == .checkpoint
                    return checkpointForStage && $0.count > $0.reusedCount + $0.failedCount
                }
                guard built else { continue }
                result[span.stage] = PerformanceStageTiming(span: span, document: previous)
                pending.remove(span.stage)
            }
        }
        return result
    }
}
