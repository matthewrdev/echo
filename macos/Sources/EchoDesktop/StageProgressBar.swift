import SwiftUI

struct StageProgressBar: View {
    let progress: RunProgress
    let showsLegend: Bool

    private var colors: [Color] { [.blue, .teal, .orange, .purple] }

    var body: some View {
        if let steps = progress.steps, let active = progress.activeStepIndex {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 3) {
                    ForEach(steps.indices, id: \.self) { index in
                        GeometryReader { geometry in
                            ZStack(alignment: .leading) {
                                Capsule().fill(colors[index % colors.count].opacity(0.16))
                                Capsule().fill(colors[index % colors.count])
                                    .frame(width: geometry.size.width * progress.fractionForStep(index))
                            }
                        }
                        .frame(height: 7)
                        .help("\(steps[index]): \(Int(progress.fractionForStep(index) * 100))% of this step")
                    }
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Depth progress across all steps")
                .accessibilityValue(
                    "\(Int((progress.overallFractionCompleted ?? 0) * 100)) percent. \(progress.stepSummary ?? "")")
                if showsLegend {
                    ViewThatFits(in: .horizontal) {
                        HStack(spacing: 14) {
                            ForEach(steps.indices, id: \.self) { index in
                                legend(steps[index], index: index, active: active)
                            }
                        }
                        VStack(alignment: .leading, spacing: 5) {
                            ForEach(steps.indices, id: \.self) { index in
                                legend(steps[index], index: index, active: active)
                            }
                        }
                    }
                }
            }
        }
    }

    private func legend(_ title: String, index: Int, active: Int) -> some View {
        HStack(spacing: 5) {
            Image(
                systemName: index < active
                    ? "checkmark.circle.fill" : index == active ? "circle.inset.filled" : "circle"
            )
            .foregroundStyle(colors[index % colors.count])
            Text(title).fontWeight(index == active ? .semibold : .regular)
        }
        .font(.caption2).foregroundStyle(index == active ? .primary : .secondary)
        .fixedSize()
    }
}
