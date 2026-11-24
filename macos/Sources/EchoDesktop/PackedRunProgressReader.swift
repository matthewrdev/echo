import Foundation

struct PackedRunProgressReader {
    func read(_ data: Data) throws -> RunProgress {
        var reader = BinaryJournalCursor(data)
        guard try reader.integer() == 0x3150_5245 else { throw malformed }
        let stageSteps = try reader.array { try $0.string() }
        let stageStepIndex = try reader.optional { Int(Int32(bitPattern: try $0.integer())) }
        let steps = try reader.array { try $0.string() }
        let stepIndex = try reader.optional { Int(Int32(bitPattern: try $0.integer())) }
        let resources = try reader.optional { try resource(&$0) }
        let operation = try reader.string()
        let unit = try reader.string()
        let operationStarted = try date(&reader)
        let phase = try reader.string()
        let stage = try reader.string()
        let message = try reader.string()
        let completed = try reader.optional { Int(Int64(bitPattern: try $0.wideInteger())) }
        let total = try reader.optional { Int(Int64(bitPattern: try $0.wideInteger())) }
        let updated = try date(&reader)
        guard reader.offset == data.count else { throw malformed }
        return RunProgress(
            stageSteps: stageSteps, stageStepIndex: stageStepIndex, steps: steps, stepIndex: stepIndex,
            operation: operation, unit: unit, operationStarted: operationStarted, resources: resources,
            phase: phase, stage: stage, message: message, completed: completed, total: total, updated: updated)
    }

    private func resource(_ reader: inout BinaryJournalCursor) throws -> ResourceProgress {
        let processor = try reader.optional { try self.processor(&$0) }
        return try ResourceProgress(
            processor: processor, sampledAt: date(&reader), workingSetBytes: signed(&reader),
            peakWorkingSetBytes: signed(&reader), managedBytes: signed(&reader), memoryBudgetBytes: signed(&reader),
            cacheResidentBytes: signed(&reader), cachePeakBytes: signed(&reader), diskReadBytes: signed(&reader),
            diskWrittenBytes: signed(&reader), pageEvictions: signed(&reader), scratchBytes: signed(&reader))
    }

    private func processor(_ reader: inout BinaryJournalCursor) throws -> ProcessorProgress {
        let cpu = try reader.optional { Double(bitPattern: try $0.wideInteger()) }
        let logical = Int(try reader.integer())
        let threads = try reader.optional { value in
            try value.array { item in
                try ThreadProgress(
                    processId: Int(item.integer()), threadId: item.wideInteger(), name: item.string(),
                    cpuPercent: Double(bitPattern: item.wideInteger()))
            }
        }
        return ProcessorProgress(cpuPercent: cpu, logicalProcessors: logical, threads: threads)
    }

    private func date(_ reader: inout BinaryJournalCursor) throws -> String {
        let ticks = try reader.wideInteger()
        let date = Date(timeIntervalSince1970: Double(ticks) / 10_000_000 - 62_135_596_800)
        return date.ISO8601Format(Date.ISO8601FormatStyle(includingFractionalSeconds: true))
    }

    private func signed(_ reader: inout BinaryJournalCursor) throws -> Int64 {
        Int64(bitPattern: try reader.wideInteger())
    }

    private var malformed: CLIError { CLIError(message: "Invalid binary run progress.") }
}
