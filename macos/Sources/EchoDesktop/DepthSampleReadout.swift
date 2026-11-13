import SwiftUI

struct DepthSampleReadout: View {
    let sample: DepthSample
    let isMetric: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if let depth = sample.depth {
                Text(
                    "Depth \(Double(depth).formatted(.number.precision(.fractionLength(2)))) \(isMetric ? "m" : "scene units")"
                )
                if let confidence = sample.confidence {
                    Text("Confidence \(Double(confidence).formatted(.percent.precision(.fractionLength(1))))")
                } else {
                    Text("Confidence unavailable")
                }
            } else {
                Text("No accepted depth")
            }
            Text("Depth pixel \(sample.x), \(sample.y)").foregroundStyle(.secondary)
        }
        .font(.caption).monospacedDigit()
        .padding(8)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
        .accessibilityElement(children: .combine)
        .allowsHitTesting(false)
    }
}
