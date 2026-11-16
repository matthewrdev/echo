import SwiftUI

struct CloudDisplayControls: View {
    @ObservedObject var model: ArtifactViewModel
    let hasSavedCloud: Bool

    private var agreementLabel: String {
        model.depthAgreementPercentage == 0 ? "All" : "≥\(Int(model.depthAgreementPercentage))%"
    }

    var body: some View {
        HStack(spacing: 24) {
            HStack {
                Text("Depth agreement").fixedSize()
                Slider(
                    value: $model.depthAgreementPercentage, in: 0...80, step: 1,
                    onEditingChanged: reloadAfterEditing
                )
                .frame(maxWidth: 260)
                .accessibilityLabel("Minimum depth agreement")
                .accessibilityValue(model.depthAgreementPercentage == 0 ? "All points" : agreementLabel)
                Text(agreementLabel).monospacedDigit().frame(width: 46, alignment: .trailing)
            }
            .help(
                "Fraction of tested neighbouring depth maps that agree with each point, from all points to at least 80%. Filters the display only; not a probability of correctness."
            )
            if hasSavedCloud {
                HStack {
                    Text("Point detail").fixedSize()
                    Slider(
                        value: $model.pointPercentage, in: 1...100, step: 1,
                        onEditingChanged: reloadAfterEditing
                    ).frame(maxWidth: 260).accessibilityLabel("Percentage of saved points")
                    Text("\(Int(model.pointPercentage))%")
                        .monospacedDigit().frame(width: 40, alignment: .trailing)
                }
            }
        }.font(.caption)
    }

    private func reloadAfterEditing(_ editing: Bool) {
        if !editing { Task { await model.load() } }
    }
}
