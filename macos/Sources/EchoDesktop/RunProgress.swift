import Foundation

struct RunProgress: Decodable {
    var stageSteps: [String]? = nil
    var stageStepIndex: Int? = nil
    var steps: [String]? = nil
    var stepIndex: Int? = nil
    var operation: String? = nil
    var unit: String? = nil
    var operationStarted: String? = nil
    var resources: ResourceProgress? = nil
    var phase: String? = nil
    let stage: String
    let message: String
    let completed: Int?
    let total: Int?
    let updated: String?

    var fractionCompleted: Double? {
        guard let completed, let total, total > 0 else { return nil }
        return min(1, max(0, Double(completed) / Double(total)))
    }

    var activeStepIndex: Int? {
        guard let steps, let stepIndex, steps.indices.contains(stepIndex) else { return nil }
        return stepIndex
    }

    var activeStageStepIndex: Int? {
        guard let stageSteps, let stageStepIndex, stageSteps.indices.contains(stageStepIndex) else { return nil }
        return stageStepIndex
    }

    var hasStepPlan: Bool { activeStageStepIndex != nil || activeStepIndex != nil }

    var stageStepSummary: String? {
        guard let stageSteps, let index = activeStageStepIndex else { return nil }
        return "\(stageSteps[index]) · step \(index + 1) of \(stageSteps.count)"
    }

    var stepSummary: String? {
        guard let steps, let index = activeStepIndex else { return nil }
        return "\(steps[index]) · step \(index + 1) of \(steps.count)"
    }

    var stepActivityTitle: String? {
        guard let summary = stageStepSummary ?? stepSummary else { return nil }
        let title = PipelineStage(rawValue: stage)?.title ?? stage.capitalized
        return "\(title) · \(summary)"
    }

    var overallFractionCompleted: Double? {
        guard let steps, let index = activeStepIndex else { return fractionCompleted }
        return (Double(index) + (fractionCompleted ?? 0)) / Double(steps.count)
    }

    func fractionForStep(_ index: Int) -> Double {
        guard let active = activeStepIndex else { return 0 }
        if index < active { return 1 }
        if index > active { return 0 }
        return fractionCompleted ?? 0
    }

    var activityDescription: String {
        var lines: [String] = []
        if let operation, !operation.isEmpty { lines.append(operation) }
        if !message.isEmpty { lines.append(message) }
        if let completed, let total, total > 0 {
            lines.append("\(completed.formatted()) of \(total.formatted()) \(unit ?? "items")")
        }
        if let resources { lines.append(resources.summary) }
        return lines.joined(separator: "\n")
    }

    var countSummary: String? {
        guard let completed, let total, let fractionCompleted else { return nil }
        let scope = activeStepIndex == nil ? "" : " of this step"
        return
            "\(completed.formatted()) of \(total.formatted()) \(unit ?? "items") · \(Int(fractionCompleted * 100))%\(scope)"
    }

    var updatedAt: Date? {
        guard let updated else { return nil }
        let parser = ISO8601DateFormatter()
        parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = parser.date(from: updated) { return date }
        parser.formatOptions = [.withInternetDateTime]
        return parser.date(from: updated)
    }
}
