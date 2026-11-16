import Charts
import SwiftUI

struct TelemetryView: View {
    @ObservedObject var history: TelemetryHistory
    let isWorking: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let latest = history.samples.last {
                HStack(alignment: .top, spacing: 24) {
                    memoryChart(latest)
                    cpuChart(latest)
                    threadChart(latest)
                }
                .frame(height: 195)
                HStack {
                    Text("Last 10 minutes · run and registered workers")
                    Spacer()
                    TimelineView(.periodic(from: .now, by: 1)) { context in
                        let age = max(0, Int(context.date.timeIntervalSince(latest.time)))
                        Text(isWorking ? "Updated \(age)s ago" : "Last recorded sample")
                            .foregroundStyle(isWorking && age > 15 ? Color.orange : Color.secondary)
                    }
                }
                .font(.caption).foregroundStyle(.secondary)
            } else {
                Text(isWorking ? "Waiting for resource measurements…" : "Telemetry appears when processing starts.")
                    .foregroundStyle(.secondary).frame(maxWidth: .infinity, minHeight: 90)
            }
        }
        .padding(.top, 8)
        .accessibilityIdentifier("processing-telemetry")
    }

    private func memoryChart(_ latest: TelemetrySample) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Memory · \(latest.memoryGiB, specifier: "%.2f") / \(latest.budgetGiB, specifier: "%.1f") GiB")
                .font(.subheadline.weight(.semibold))
            Chart(history.samples) { sample in
                LineMark(x: .value("Time", sample.time), y: .value("GiB", sample.memoryGiB))
                    .foregroundStyle(by: .value("Memory", "RAM"))
                LineMark(x: .value("Time", sample.time), y: .value("GiB", sample.cacheGiB))
                    .foregroundStyle(by: .value("Memory", "Cache"))
                PointMark(x: .value("Time", sample.time), y: .value("GiB", sample.memoryGiB))
                    .foregroundStyle(.blue).symbolSize(8)
            }
            .chartOverlay { proxy in
                GeometryReader { geometry in
                    if let frame = proxy.plotFrame, let y = proxy.position(forY: latest.budgetGiB) {
                        Path { path in
                            let plot = geometry[frame]
                            path.move(to: CGPoint(x: plot.minX, y: plot.minY + y))
                            path.addLine(to: CGPoint(x: plot.maxX, y: plot.minY + y))
                        }.stroke(.secondary, style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
                    }
                }
            }
            .chartYScale(domain: 0...max(latest.budgetGiB, history.samples.map(\.memoryGiB).max() ?? 1) * 1.1)
            .chartXAxis { AxisMarks(values: .automatic(desiredCount: 3)) }
            .chartForegroundStyleScale(["RAM": Color.blue, "Cache": Color.teal])
            Text(
                "Peak \(Double(latest.resources.peakWorkingSetBytes) / 1_073_741_824, specifier: "%.2f") GiB · dashed line: budget"
            )
            .font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
    }

    private func cpuChart(_ latest: TelemetrySample) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("CPU").font(.subheadline.weight(.semibold))
                if let cpu = latest.resources.processor?.capacityPercent {
                    Text("\(cpu, specifier: "%.0f")% of capacity").font(.subheadline.monospacedDigit())
                }
            }
            if history.samples.contains(where: { $0.resources.processor?.capacityPercent != nil }) {
                Chart(history.samples) { sample in
                    if let cpu = sample.resources.processor?.capacityPercent {
                        LineMark(x: .value("Time", sample.time), y: .value("CPU %", cpu))
                            .foregroundStyle(.orange)
                        PointMark(x: .value("Time", sample.time), y: .value("CPU %", cpu))
                            .foregroundStyle(.orange).symbolSize(8)
                    }
                }
                .chartYScale(domain: 0...100)
                .chartXAxis { AxisMarks(values: .automatic(desiredCount: 3)) }
            } else {
                Text("CPU measurements unavailable").foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            Text(
                "100% = all \(latest.resources.processor?.logicalProcessors ?? ProcessInfo.processInfo.activeProcessorCount) logical cores"
            )
            .font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
    }

    private func threadChart(_ latest: TelemetrySample) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("CPU by thread").font(.subheadline.weight(.semibold))
            if let threads = latest.resources.processor?.threads, !threads.isEmpty {
                ScrollView {
                    LazyVStack(spacing: 5) {
                        ForEach(threads.sorted { $0.cpuPercent > $1.cpuPercent }) { thread in
                            HStack(spacing: 6) {
                                Text(thread.label).lineLimit(1).frame(width: 110, alignment: .leading)
                                ProgressView(value: min(100, max(0, thread.cpuPercent)), total: 100).tint(.orange)
                                Text("\(thread.cpuPercent, specifier: "%.0f")%")
                                    .monospacedDigit().frame(width: 35, alignment: .trailing)
                            }
                            .help("Process \(thread.processId) · thread \(thread.threadId) · \(thread.name)")
                            .accessibilityElement(children: .combine)
                        }
                    }.font(.caption)
                }
                Text("\(threads.count) threads · 100% = one core")
                    .font(.caption).foregroundStyle(.secondary)
            } else {
                Text("Thread measurements unavailable").foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(maxWidth: .infinity)
    }
}
