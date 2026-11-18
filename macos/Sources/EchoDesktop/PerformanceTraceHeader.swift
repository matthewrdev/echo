import SwiftUI

struct PerformanceTraceHeader: View {
    @ObservedObject var model: PerformanceTraceViewModel
    @Binding var page: PerformanceTracePage

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 16) {
                Picker("Attempt", selection: $model.selected) {
                    ForEach(model.attempts) { Text($0.label).tag($0.id) }
                }.frame(maxWidth: 430, alignment: .leading)
                Picker("Compare", selection: $model.comparison) {
                    Text("None").tag("")
                    ForEach(model.attempts.filter { $0.id != model.selected }) { Text($0.label).tag($0.id) }
                }.frame(maxWidth: 430, alignment: .leading)
                Spacer(minLength: 0)
                Button("Export JSON", systemImage: "square.and.arrow.up") { model.export() }
                    .labelStyle(.iconOnly).disabled(model.document == nil).help("Export trace as JSON")
            }
            if let document = model.document {
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 20) {
                        pagePicker
                        Spacer(minLength: 0)
                        summary(document)
                    }
                    VStack(alignment: .leading, spacing: 10) {
                        pagePicker
                        summary(document)
                    }
                }
            }
        }.controlSize(.small)
    }

    private var pagePicker: some View {
        Picker("View", selection: $page) {
            ForEach(PerformanceTracePage.allCases, id: \.self) { Text($0.rawValue).tag($0) }
        }.pickerStyle(.segmented).labelsHidden().frame(width: 390, alignment: .leading)
    }

    private func summary(_ document: PerformanceTraceDocument) -> some View {
        let value = document.summary
        let gpu = document.statistics.compactMap(\.gpuMilliseconds).reduce(0, +)
        return HStack(spacing: 16) {
            Text(value.status.rawValue.capitalized).fontWeight(.semibold)
            Text("Elapsed \(PerformanceDuration.format(value.durationMilliseconds))")
            Text("CPU \(PerformanceDuration.format(value.cpuMilliseconds))")
            Text("GPU \(PerformanceDuration.format(gpu))").help("Completed GPU command buffers")
            if let baseline = model.baseline {
                Text(
                    "Δ \(PerformanceDuration.format(value.durationMilliseconds - baseline.summary.durationMilliseconds))"
                ).help("Elapsed time compared with the selected attempt")
            }
        }.font(.caption.monospacedDigit()).fixedSize(horizontal: true, vertical: false)
    }
}
