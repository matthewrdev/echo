import SwiftUI

struct PerformanceDiagnosticsView: View {
    let document: PerformanceTraceDocument
    @State private var stage: PipelineStage?
    @State private var kind = PerformanceSpanKind.operation

    private var rows: [PerformanceStatistic] {
        document.statistics.filter { $0.kind == kind && (stage == nil || $0.stage == stage?.rawValue) }
            .sorted { $0.totalMilliseconds > $1.totalMilliseconds }
    }

    private var measurements: [PerformanceMeasurement] {
        (document.measurements ?? []).filter { stage == nil || $0.stage == stage?.rawValue }
            .sorted { $0.id < $1.id }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let environment = document.summary.environment {
                Text(
                    "\(environment.runtime) · \(environment.architecture) · \(environment.logicalProcessors) logical processors · \(environment.serverGc ? "Server" : "Workstation") GC · \(environment.operatingSystem)"
                ).font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
            }
            HStack {
                Picker("Detail", selection: $kind) {
                    ForEach(PerformanceSpanKind.allCases, id: \.self) { Text($0.rawValue.capitalized).tag($0) }
                }.pickerStyle(.segmented)
                Picker("Stage", selection: $stage) {
                    Text("All stages").tag(nil as PipelineStage?)
                    ForEach(PipelineStage.allCases) { Text($0.shortTitle).tag(Optional($0)) }
                }.frame(width: 200)
            }
            Table(rows) {
                TableColumn("Step") { row in
                    VStack(alignment: .leading) {
                        Text(row.name)
                        Text(row.stage.capitalized).foregroundStyle(.secondary)
                    }
                }.width(min: 160, ideal: 230)
                TableColumn("Calls") { Text($0.count.formatted()) }.width(55)
                TableColumn("Covered") { Text(duration($0.busyMilliseconds)) }
                TableColumn("Sum") { Text(duration($0.totalMilliseconds)) }
                TableColumn("P95 ≤") { Text(duration($0.p95UpperMilliseconds)) }
                TableColumn("Peak active") { Text($0.maximumConcurrency.map(String.init) ?? "—") }
                TableColumn("Allocated") { row in
                    Text(bytes(row.processAllocatedBytes ?? row.threadAllocatedBytes))
                        .help("Thread samples: \(row.threadAllocationSamples ?? 0) of \(row.count) calls")
                }
                TableColumn("CPU / GC pause") { row in
                    Text("\(duration(row.processCpuMilliseconds)) / \(duration(row.gcPauseMilliseconds))")
                        .help(
                            "GC collections: \(row.gen0Collections ?? 0) / \(row.gen1Collections ?? 0) / \(row.gen2Collections ?? 0)"
                        )
                }
            }.font(.caption.monospacedDigit()).frame(minHeight: 170)
            Text(
                "Covered time counts overlapping calls once; Sum includes overlap and child work. P95 is a power-of-two histogram upper bound. Stage allocations, CPU and GC are process-wide; other allocation samples cover only the calling thread and include children. These are allocated bytes, not retained memory. Older traces show —."
            ).font(.caption).foregroundStyle(.secondary)
            Text("Workload and scheduling measurements").font(.headline)
            Table(measurements) {
                TableColumn("Measurement") { row in
                    VStack(alignment: .leading) {
                        Text(row.name)
                        Text(row.stage.capitalized).foregroundStyle(.secondary)
                    }
                }.width(min: 190, ideal: 290)
                TableColumn("Samples") { Text($0.count.formatted()) }.width(65)
                TableColumn("Unit") { Text($0.unit) }.width(65)
                TableColumn("Total") { Text(number($0.total)) }
                TableColumn("Mean") { Text(number($0.mean)) }
                TableColumn("Min / max") { Text("\(number($0.minimum)) / \(number($0.maximum))") }
            }.font(.caption.monospacedDigit()).frame(minHeight: 130)
            Text(
                "Measurements are aggregated without individual timeline records; time samples can overlap across workers."
            )
            .font(.caption).foregroundStyle(.secondary)
            if let dropped = document.summary.droppedMeasurements, dropped > 0 {
                Text("\(dropped.formatted()) samples omitted after the 256 measurement-name limit.")
                    .font(.caption).foregroundStyle(.orange)
            }
        }
    }

    private func duration(_ value: Double?) -> String { value.map(PerformanceDuration.format) ?? "—" }
    private func bytes(_ value: Int64?) -> String {
        value.map { ByteCountFormatter.string(fromByteCount: $0, countStyle: .binary) } ?? "—"
    }
    private func number(_ value: Double) -> String { value.formatted(.number.precision(.fractionLength(0...3))) }
}
