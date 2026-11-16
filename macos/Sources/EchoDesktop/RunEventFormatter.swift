import Foundation

struct RunEventFormatter {
    func entry(from data: Data) -> RunLogEntry? {
        guard let record = try? JSONDecoder().decode(RunEventRecord.self, from: data) else { return nil }
        let payload = record.payload
        let rawStage = payload["stage"]?.description ?? payload["target"]?.description ?? "run"
        let stage = rawStage == "cloudPreview" ? "cloud" : rawStage
        let title = PipelineStage(rawValue: stage)?.title ?? stage
        let message: String
        switch record.type {
        case "stageProgress": message = payload["message"]?.description ?? "Working…"
        case "stageStarted":
            guard PipelineStage(rawValue: stage) != nil else { return nil }
            message = "Starting \(title.lowercased())…"
        case "imageCheckpoint", "pairCheckpoint":
            let unit = record.type == "pairCheckpoint" ? "image pairs" : "images"
            let completed = number(payload["completed"])
            let total = number(payload["total"])
            message = payload["message"]?.description ?? "\(completed) of \(total) \(unit) processed."
        case "checkpoint":
            if rawStage == "cloudPreview" {
                message = "Live dense-cloud preview updated."
            } else if PipelineStage(rawValue: stage) != nil {
                message = "\(title) saved and ready to view."
            } else {
                return nil
            }
        case "artifactReused":
            guard PipelineStage(rawValue: stage) != nil else { return nil }
            message = "Reused saved \(title.lowercased()); no recomputation needed."
        case "runStarted":
            message = "Processing through \(title.lowercased()). Completed compatible work will be reused."
        case "runCompleted": message = "Processing finished. All requested outputs are saved."
        case "runCancelled":
            message =
                "Processing stopped. Saved results are still available. Resume reuses saved work and repeats unfinished steps."
        case "runFailed": message = payload["error"]?.description ?? "Processing failed."
        default: return nil
        }
        return RunLogEntry(id: record.sequence, timestamp: record.timestamp, stage: stage, message: message)
    }

    private func number(_ value: JSONValue?) -> String {
        if case .number(let number) = value { return Int(number).formatted() }
        return "0"
    }
}
