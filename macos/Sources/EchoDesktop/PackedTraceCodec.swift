import Foundation

struct PackedTraceCodec {
    private let dates = ISO8601DateFormatter()

    func summary(_ cursor: inout BinaryJournalCursor) throws -> PerformanceTraceSummary {
        guard try cursor.integer() == 0x3148_5445 else { throw malformed }
        let dropped = try signed(&cursor)
        let environment = try cursor.optional {
            try PerformanceTraceEnvironment(
                runtime: $0.string(), operatingSystem: $0.string(), architecture: $0.string(),
                logicalProcessors: Int($0.integer()), serverGc: $0.byte() != 0)
        }
        return try PerformanceTraceSummary(
            environment: environment, droppedMeasurements: dropped, id: cursor.string(), runId: cursor.string(),
            target: cursor.string(), startedAt: date(cursor.wideInteger()), updatedAt: date(cursor.wideInteger()),
            status: element(PerformanceStatus.allCases, &cursor), durationMilliseconds: number(&cursor),
            cpuMilliseconds: number(&cursor), droppedSpans: Int(signed(&cursor)))
    }

    func span(_ cursor: inout BinaryJournalCursor) throws -> PerformanceSpan {
        let threadAllocated = try cursor.optional(signed)
        let processAllocated = try cursor.optional(signed)
        let cpu = try cursor.optional(number)
        let pause = try cursor.optional(number)
        let gen0 = try cursor.optional(signed)
        let gen1 = try cursor.optional(signed)
        let gen2 = try cursor.optional(signed)
        return try PerformanceSpan(
            id: signed(&cursor), parentId: cursor.optional(signed), stage: cursor.string(), name: cursor.string(),
            kind: element(PerformanceSpanKind.allCases, &cursor),
            backend: element(PerformanceBackend.allCases, &cursor),
            status: element(PerformanceStatus.allCases, &cursor), threadId: Int(cursor.integer()),
            startMilliseconds: number(&cursor), durationMilliseconds: number(&cursor),
            gpuMilliseconds: cursor.optional(number), items: cursor.optional(signed),
            totalItems: cursor.optional(signed),
            unit: cursor.string(), bytes: signed(&cursor), detail: cursor.optional { try $0.string() },
            threadAllocatedBytes: threadAllocated, processAllocatedBytes: processAllocated, processCpuMilliseconds: cpu,
            gcPauseMilliseconds: pause, gen0Collections: gen0, gen1Collections: gen1, gen2Collections: gen2)
    }

    func statistic(_ cursor: inout BinaryJournalCursor) throws -> PerformanceStatistic {
        let busy = try cursor.optional(number)
        let concurrency = try cursor.optional { try Int($0.integer()) }
        let median = try cursor.optional(number)
        let p95 = try cursor.optional(number)
        let threadAllocated = try cursor.optional(signed)
        let allocationSamples = try signed(&cursor)
        let processAllocated = try cursor.optional(signed)
        let cpu = try cursor.optional(number)
        let pause = try cursor.optional(number)
        let gen0 = try cursor.optional(signed)
        let gen1 = try cursor.optional(signed)
        let gen2 = try cursor.optional(signed)
        return try PerformanceStatistic(
            stage: cursor.string(), name: cursor.string(), kind: element(PerformanceSpanKind.allCases, &cursor),
            backend: element(PerformanceBackend.allCases, &cursor), count: signed(&cursor),
            reusedCount: signed(&cursor), failedCount: signed(&cursor), totalMilliseconds: number(&cursor),
            maximumMilliseconds: number(&cursor), gpuMilliseconds: cursor.optional(number), items: signed(&cursor),
            bytes: signed(&cursor), busyMilliseconds: busy, maximumConcurrency: concurrency,
            medianUpperMilliseconds: median, p95UpperMilliseconds: p95, threadAllocatedBytes: threadAllocated,
            threadAllocationSamples: allocationSamples, processAllocatedBytes: processAllocated,
            processCpuMilliseconds: cpu, gcPauseMilliseconds: pause, gen0Collections: gen0, gen1Collections: gen1,
            gen2Collections: gen2)
    }

    func measurement(_ cursor: inout BinaryJournalCursor) throws -> PerformanceMeasurement {
        try PerformanceMeasurement(
            stage: cursor.string(), name: cursor.string(), unit: cursor.string(), count: signed(&cursor),
            total: number(&cursor), minimum: number(&cursor), maximum: number(&cursor))
    }

    private func number(_ cursor: inout BinaryJournalCursor) throws -> Double {
        Double(bitPattern: try cursor.wideInteger())
    }

    private func signed(_ cursor: inout BinaryJournalCursor) throws -> Int64 {
        Int64(bitPattern: try cursor.wideInteger())
    }

    private func element<T>(_ values: [T], _ cursor: inout BinaryJournalCursor) throws -> T {
        let index = Int(try cursor.byte())
        guard index < values.count else { throw malformed }
        return values[index]
    }

    private func date(_ ticks: UInt64) -> String {
        let second = Double(ticks / 10_000_000) - 62_135_596_800
        let base = dates.string(from: Date(timeIntervalSince1970: second))
        let fraction = String(format: "%07llu", ticks % 10_000_000)
        return base.hasSuffix("Z") ? "\(base.dropLast()).\(fraction)Z" : base
    }

    var malformed: CLIError { CLIError(message: "Invalid packed performance trace.") }
}
