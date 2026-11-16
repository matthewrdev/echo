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
                    Toggle("Auto", isOn: $model.automaticPointDetail).toggleStyle(.checkbox)
                        .onChange(of: model.automaticPointDetail) { _, _ in Task { await model.load() } }
                    if !model.automaticPointDetail {
                        Slider(
                            value: $model.pointPercentage, in: 1...100, step: 1,
                            onEditingChanged: reloadAfterEditing
                        ).frame(maxWidth: 260).accessibilityLabel("Point preview detail")
                            .accessibilityValue("Up to \(model.pointLimit.formatted()) points")
                        Text(model.pointLimit.formatted(.number.notation(.compactName)))
                            .monospacedDigit().frame(width: 52, alignment: .trailing)
                    }
                }
                .help(
                    "Auto displays up to one million points across the whole cloud. Manual detail supports up to four million. This changes only the preview; all saved points remain available for processing and export."
                )
            }
        }.font(.caption)
    }

    private func reloadAfterEditing(_ editing: Bool) {
        if !editing { Task { await model.load() } }
    }
}
