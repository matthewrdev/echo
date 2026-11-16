import SwiftUI

struct FeatureOverlayControls: View {
    @Binding var opacity: Double
    @Binding var showsPoints: Bool
    @Binding var showsLines: Bool
    @Binding var showsJunctions: Bool

    var body: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 16) {
                layers
                opacityControl
            }
            VStack(alignment: .leading, spacing: 8) {
                layers
                opacityControl
            }
        }
        .font(.caption).controlSize(.small)
    }

    private var layers: some View {
        HStack(spacing: 12) {
            Toggle("Points", isOn: $showsPoints)
            Toggle("Lines", isOn: $showsLines)
            Toggle("Junctions", isOn: $showsJunctions)
        }
        .toggleStyle(.checkbox).fixedSize()
    }

    private var opacityControl: some View {
        HStack(spacing: 8) {
            Text("Opacity")
            Slider(value: $opacity, in: 0...1) { Text("Feature overlay opacity") }
                .labelsHidden().frame(width: 120)
                .help("Fade points, lines, and junctions over the photograph.")
            Text(opacity, format: .percent.precision(.fractionLength(0)))
                .monospacedDigit().frame(width: 38, alignment: .trailing)
        }
        .fixedSize()
    }
}
