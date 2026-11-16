import SwiftUI

struct PerformanceTraceView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var model: PerformanceTraceViewModel
    @State private var kind = PerformanceSpanKind.stage
    @State private var stage: PipelineStage?
    @State private var sorting = [KeyPathComparator(\PerformanceStatistic.totalMilliseconds, order: .reverse)]

    private var spans: [PerformanceSpan] {
        (model.document?.spans ?? []).filter { $0.kind == kind && (stage == nil || $0.stage == stage?.rawValue) }
    }
    private var rows: [PerformanceStatistic] {
        (model.document?.statistics ?? []).filter { $0.kind == kind && (stage == nil || $0.stage == stage?.rawValue) }
            .sorted(using: sorting)
    }
    private var baseline: [String: PerformanceStatistic] {
        Dictionary(uniqueKeysWithValues: (model.baseline?.statistics ?? []).map { ($0.id, $0) })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let error = model.error { Text(error).foregroundStyle(.red).textSelection(.enabled) }
            if model.attempts.isEmpty {
                ContentUnavailableView(
                    "No performance traces yet", systemImage: "chart.bar.xaxis",
                    description:
                        Text("New processing runs and resumes record a trace. Earlier runs have no recorded timings."))
            } else {
                HStack {
                    Picker("Attempt", selection: $model.selected) {
                        ForEach(model.attempts) { Text($0.label).tag($0.id) }
                    }
                    Button("Export JSON", systemImage: "square.and.arrow.up") { model.export() }
                        .disabled(model.document == nil)
                }
                Picker("Compare with", selection: $model.comparison) {
                    Text("None").tag("")
                    ForEach(model.attempts.filter { $0.id != model.selected }) { Text($0.label).tag($0.id) }
                }
                if let document = model.document {
                    summary(document)
                    HStack {
                        Picker("Detail", selection: $kind) {
                            ForEach(PerformanceSpanKind.allCases, id: \.self) { Text($0.rawValue.capitalized).tag($0) }
                        }.pickerStyle(.segmented)
                        Picker("Stage", selection: $stage) {
                            Text("All stages").tag(nil as PipelineStage?)
                            ForEach(PipelineStage.allCases) { Text($0.shortTitle).tag(Optional($0)) }
                        }.frame(width: 200)
                    }
                    PerformanceTimeline(spans: spans, duration: document.summary.durationMilliseconds)
                    Text("Completed intervals · faded timeline bars are reused checkpoints").font(.caption)
                        .foregroundStyle(.secondary)
                    timingTable
                    Text(
                        "Elapsed intervals include child work and can overlap; do not add rows to obtain run time. GPU time is measured for completed command buffers and belongs to the call that drained them, which can include earlier dispatches. CPU time covers the processor process, not external workers."
                    )
                    .font(.caption).foregroundStyle(.secondary)
                    if document.summary.droppedSpans > 0 {
                        Text(
                            "\(document.summary.droppedSpans.formatted()) detailed intervals omitted to bound memory. Completed-operation aggregates remain available."
                        )
                        .font(.caption).foregroundStyle(.orange)
                    }
                } else {
                    ProgressView("Loading performance trace…").frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
        }
        .padding(18)
        .task(id: workspace.root) {
            while !Task.isCancelled {
                await model.refresh(root: workspace.root, preferredRun: workspace.inspection?.run?.id)
                do { try await Task.sleep(for: .seconds(5)) } catch { break }
            }
        }
        .task(id: model.selected) { await model.load() }
        .task(id: model.comparison) { await model.load(comparing: true) }
    }

    private func summary(_ document: PerformanceTraceDocument) -> some View {
        let value = document.summary
        let gpu = document.statistics.compactMap(\.gpuMilliseconds).reduce(0, +)
        return HStack(spacing: 24) {
            Text(value.status.rawValue.capitalized).fontWeight(.semibold)
            Text("Elapsed \(PerformanceDuration.format(value.durationMilliseconds))")
            Text("CPU \(PerformanceDuration.format(value.cpuMilliseconds))")
            Text("GPU batches \(PerformanceDuration.format(gpu))")
            if let baseline = model.baseline {
                Text(
                    "Elapsed Δ \(PerformanceDuration.format(value.durationMilliseconds - baseline.summary.durationMilliseconds))"
                )
            }
        }.font(.callout.monospacedDigit())
    }

    private var timingTable: some View {
        Table(rows, sortOrder: $sorting) {
            TableColumn("Step", value: \.name) { row in
                VStack(alignment: .leading) {
                    Text(row.name)
                    Text(row.stage.capitalized).font(.caption2).foregroundStyle(.secondary)
                }
            }.width(min: 140, ideal: 220)
            TableColumn("Backend", value: \.backendLabel).width(65)
            TableColumn("Calls", value: \.count) { Text($0.count.formatted()) }.width(50)
            TableColumn("Elapsed", value: \.totalMilliseconds) {
                Text(PerformanceDuration.format($0.totalMilliseconds))
            }
            TableColumn("Longest", value: \.maximumMilliseconds) {
                Text(PerformanceDuration.format($0.maximumMilliseconds))
            }
            TableColumn("GPU batches") { row in Text(row.gpuMilliseconds.map(PerformanceDuration.format) ?? "—") }
            TableColumn("Reused / failed") { Text("\($0.reusedCount) / \($0.failedCount)") }
            TableColumn("Work", value: \.items) { row in
                Text(
                    row.bytes > 0
                        ? ByteCountFormatter.string(fromByteCount: row.bytes, countStyle: .binary)
                        : row.items.formatted())
            }
            TableColumn("Δ elapsed") { row in
                Text(
                    baseline[row.id].map { PerformanceDuration.format(row.totalMilliseconds - $0.totalMilliseconds) }
                        ?? "—")
            }
        }.font(.caption.monospacedDigit()).frame(minHeight: 120)
    }
}
