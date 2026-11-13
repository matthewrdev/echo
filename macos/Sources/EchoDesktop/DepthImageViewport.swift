import SwiftUI

struct DepthImageViewport: View {
    let data: DepthImagePresentation
    @Binding var mode: String
    @AppStorage("depthOverlayOpacity") private var overlayOpacity = 1.0
    @StateObject private var controller = CanvasViewportController()

    private func modeButton(_ title: String, icon: String) -> some View {
        Button(title, systemImage: icon) { mode = title }
            .labelStyle(ViewerIconLabelStyle(selected: mode == title)).buttonStyle(.plain)
            .help(title).accessibilityAddTraits(mode == title ? .isSelected : [])
    }

    var body: some View {
        VStack(spacing: 8) {
            HStack(spacing: 8) {
                CanvasControls(controller: controller, showsInstructions: false)
                HStack(spacing: 4) {
                    modeButton("Depth", icon: "ruler")
                    modeButton("Confidence", icon: "checkmark.shield")
                }
                Label("Overlay", systemImage: "square.3.layers.3d")
                Slider(value: $overlayOpacity, in: 0...1) { Text("Depth overlay opacity") }
                    .labelsHidden()
                    .frame(width: 140)
                    .disabled(data.photograph == nil)
                    .help("Fade the depth or confidence colours over the source photograph.")
                Text(overlayOpacity, format: .percent.precision(.fractionLength(0)))
                    .monospacedDigit().frame(width: 38, alignment: .trailing)
            }.font(.caption)
            CanvasViewport(
                renderer: DepthCanvasRenderer(data: data, overlayOpacity: overlayOpacity), controller: controller,
                reportsHover: true
            )
            .overlay {
                if let hover = controller.hover, let sample = data.map.sample(at: hover.imagePoint) {
                    PointerTooltipLayout(point: hover.viewportPoint) {
                        DepthSampleReadout(sample: sample, isMetric: data.isMetric).fixedSize()
                    }
                    .allowsHitTesting(false)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 8))
            if data.photograph == nil {
                Text("Source photograph unavailable").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}
