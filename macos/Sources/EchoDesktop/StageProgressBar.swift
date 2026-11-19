import SwiftUI

struct StageProgressBar: View {
    let progress: RunProgress
    let showsLegend: Bool

    private var colors: [Color] { [.blue, .teal, .orange, .purple] }
    private var isStagePlan: Bool { progress.activeStageStepIndex != nil }
    private var steps: [String]? { isStagePlan ? progress.stageSteps : progress.steps }
    private var active: Int? { isStagePlan ? progress.activeStageStepIndex : progress.activeStepIndex }

    private func fraction(_ index: Int) -> Double {
        // A phase contains several operations with independent counters. Mark
        // it complete only when the next phase starts, rather than jumping back
        // whenever an operation resets its own counter.
        if isStagePlan { return index < (active ?? 0) ? 1 : 0 }
        return progress.fractionForStep(index)
    }

    private func status(_ index: Int) -> String {
        if isStagePlan {
            return index < (active ?? 0) ? "Completed" : index == active ? "In progress" : "Pending"
        }
        return "\(Int(fraction(index) * 100))% of this step"
    }

    var body: some View {
        if let steps, let active {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 3) {
                    ForEach(steps.indices, id: \.self) { index in
                        GeometryReader { geometry in
                            ZStack(alignment: .leading) {
                                Capsule().fill(colors[index % colors.count].opacity(0.16))
                                Capsule().fill(colors[index % colors.count])
                                    .frame(width: geometry.size.width * fraction(index))
                                if isStagePlan && index == active {
                                    Capsule().strokeBorder(colors[index % colors.count], lineWidth: 1.5)
                                }
                            }
                        }
                        .frame(height: 7)
                        .help("\(steps[index]): \(status(index))")
                    }
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Processing steps")
                .accessibilityValue(
                    "\(isStagePlan ? progress.stageStepSummary ?? "" : progress.stepSummary ?? ""). \(status(active))."
                )
                if showsLegend {
                    ViewThatFits(in: .horizontal) {
                        HStack(spacing: 14) {
                            ForEach(steps.indices, id: \.self) { index in
                                legend(steps[index], index: index, active: active)
                            }
                        }
                        LazyVGrid(
                            columns: [GridItem(.adaptive(minimum: 150), alignment: .leading)],
                            alignment: .leading, spacing: 5
                        ) {
                            ForEach(steps.indices, id: \.self) { index in
                                legend(steps[index], index: index, active: active)
                            }
                        }
                    }
                }
                if showsLegend && isStagePlan {
                    if let steps = progress.steps, let active = progress.activeStepIndex {
                        HStack(spacing: 3) {
                            ForEach(steps.indices, id: \.self) { index in
                                GeometryReader { geometry in
                                    ZStack(alignment: .leading) {
                                        Capsule().fill(colors[index % colors.count].opacity(0.16))
                                        Capsule().fill(colors[index % colors.count])
                                            .frame(width: geometry.size.width * progress.fractionForStep(index))
                                        if index == active && progress.fractionCompleted == nil {
                                            Capsule().strokeBorder(colors[index % colors.count], lineWidth: 1.5)
                                        }
                                    }
                                }.frame(height: 7).help(steps[index])
                            }
                        }
                        .accessibilityLabel("Current stage substeps")
                        .accessibilityValue(progress.stepSummary ?? "")
                        LazyVGrid(
                            columns: [GridItem(.adaptive(minimum: 150), alignment: .leading)],
                            alignment: .leading, spacing: 5
                        ) {
                            ForEach(steps.indices, id: \.self) { index in
                                legend(steps[index], index: index, active: active)
                            }
                        }
                    }
                    HStack(spacing: 10) {
                        if let fraction = progress.fractionCompleted {
                            ProgressView(value: fraction)
                                .progressViewStyle(.linear).controlSize(.small).tint(.secondary)
                                .frame(width: 140)
                                .accessibilityLabel(progress.operation ?? "Current step")
                                .help(progress.countSummary ?? progress.message)
                        }
                    }
                    .font(.caption).foregroundStyle(.secondary)
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
        .fixedSize(horizontal: false, vertical: true)
    }
}
