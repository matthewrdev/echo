import SwiftUI

struct DepthQualityBadge: View {
    let summary: DepthQualitySummary
    @State private var showingDetails = false

    private var colour: Color {
        switch summary.grade {
        case .empty, .poor: return .red
        case .limited, .partial: return .orange
        case .broad: return .blue
        }
    }

    var body: some View {
        Button {
            showingDetails.toggle()
        } label: {
            Label(summary.grade.rawValue, systemImage: "gauge.with.dots.needle.33percent")
                .foregroundStyle(colour)
        }
        .buttonStyle(.plain)
        .help("Depth health: \(summary.grade.rawValue). Click for coverage and matching statistics.")
        .accessibilityLabel("Depth health: \(summary.grade.rawValue)")
        .popover(isPresented: $showingDetails) {
            VStack(alignment: .leading, spacing: 10) {
                Text("Depth health · \(summary.grade.rawValue)").font(.headline)
                LabeledContent(
                    "Accepted pixels", value: "\(summary.accepted.formatted()) / \(summary.total.formatted())")
                LabeledContent("Coverage", value: summary.coverage.formatted(.percent.precision(.fractionLength(2))))
                LabeledContent("Covered regions", value: "\(summary.coveredRegions) / \(summary.regionCount)")
                LabeledContent(
                    "Mean matching confidence",
                    value: summary.meanConfidence?.formatted(.percent.precision(.fractionLength(1))) ?? "Unavailable")
                Divider()
                Text(
                    "Coverage grade: Poor <5%, Limited <25%, Partial <50%, Broad ≥50%. Sky and occluded areas count as uncovered. A region is covered when at least 10% of its pixels have depth."
                )
                Text(
                    "Matching confidence measures patch similarity, not accuracy. Cross-view geometric consistency has not been assessed by this grade."
                )
                .foregroundStyle(.secondary)
            }
            .font(.caption).padding(14).frame(width: 360)
        }
    }
}
