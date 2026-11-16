import SwiftUI

struct DepthQualityBadge: View {
    let summary: DepthQualitySummary
    @State private var showingDetails = false

    private var colour: Color {
        switch summary.grade {
        case .empty, .poor: return .red
        case .limited, .partial: return .orange
        case .broad: return .blue
        case .strong, .dense: return .green
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
        .help("Depth coverage: \(summary.grade.rawValue). Click for coverage and matching statistics.")
        .accessibilityLabel("Depth coverage: \(summary.grade.rawValue)")
        .popover(isPresented: $showingDetails) {
            VStack(alignment: .leading, spacing: 10) {
                Text("Depth coverage · \(summary.grade.rawValue)").font(.headline)
                if let coverage = summary.surfaceCoverage {
                    LabeledContent(
                        "Surface coverage", value: coverage.formatted(.percent.precision(.fractionLength(2))))
                    LabeledContent(
                        "Accepted surface pixels",
                        value: "\(summary.surfaceAccepted.formatted()) / \(summary.surfaceTotal.formatted())")
                    LabeledContent("Excluded sky pixels", value: summary.excludedSkyPixels.formatted())
                } else {
                    Text("Sky mask unavailable. The grade uses the whole image.").foregroundStyle(.secondary)
                }
                LabeledContent(
                    "Accepted image pixels", value: "\(summary.accepted.formatted()) / \(summary.total.formatted())")
                LabeledContent(
                    "Whole-image coverage", value: summary.coverage.formatted(.percent.precision(.fractionLength(2))))
                LabeledContent("Covered regions", value: "\(summary.coveredRegions) / \(summary.regionCount)")
                LabeledContent(
                    "Mean matching confidence",
                    value: summary.meanConfidence?.formatted(.percent.precision(.fractionLength(1))) ?? "Unavailable")
                Divider()
                Text(
                    "Coverage grade: Empty 0%, Poor 0–<5%, Limited 5–<25%, Partial 25–<50%, Broad 50–<65%, Strong 65–<80%, Dense ≥80%. Confident sky is excluded when a matching sky mask is available. Uncertain sky, occlusions and other gaps still count as uncovered. A region is covered when at least 10% of its eligible pixels have depth."
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
