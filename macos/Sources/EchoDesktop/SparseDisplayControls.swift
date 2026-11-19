import SwiftUI

struct SparseDisplayControls: View {
    @ObservedObject var model: ArtifactViewModel

    var body: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 24) {
                quality
                detail
            }
            VStack(alignment: .leading, spacing: 8) {
                quality
                detail
            }
        }
        .font(.caption).controlSize(.small)
    }

    private var quality: some View {
        HStack(spacing: 8) {
            Text("Reprojection error")
            Toggle("All", isOn: $model.allSparseErrors).toggleStyle(.checkbox)
                .onChange(of: model.allSparseErrors) { _, _ in Task { await model.load() } }
            if !model.allSparseErrors {
                Slider(
                    value: $model.maximumSparseError, in: 0.25...20, step: 0.25,
                    onEditingChanged: reloadAfterEditing
                )
                .frame(width: 140).accessibilityLabel("Maximum sparse reprojection error")
                Text("≤\(model.maximumSparseError.formatted(.number.precision(.fractionLength(2)))) px")
                    .monospacedDigit().frame(width: 80, alignment: .trailing)
            }
        }
        .fixedSize()
        .help(
            "Lower error means the reconstructed point projects closer to its measured photograph feature. All disables this display filter. Error is measured in pixels, not a probability of correctness."
        )
    }

    private var detail: some View {
        HStack(spacing: 8) {
            Picker("Point detail", selection: $model.sparsePointDetail) {
                ForEach(SparsePointDetail.allCases, id: \.self) { Text($0.rawValue) }
            }
            .fixedSize()
            .onChange(of: model.sparsePointDetail) { _, _ in Task { await model.load() } }
            if model.sparsePointDetail == .custom {
                Slider(
                    value: $model.sparsePointPercentage, in: 1...100, step: 1,
                    onEditingChanged: reloadAfterEditing
                )
                .frame(width: 140).accessibilityLabel("Sparse point display percentage")
                Text("\(Int(model.sparsePointPercentage))%")
                    .monospacedDigit().frame(width: 38, alignment: .trailing)
            }
        }
        .fixedSize()
        .help(
            "Auto displays up to one million points. All and Custom support up to four million in saved clouds and new live alignments. Older live previews may contain a smaller sample. Filters affect only the display."
        )
    }

    private func reloadAfterEditing(_ editing: Bool) {
        if !editing { Task { await model.load() } }
    }
}
