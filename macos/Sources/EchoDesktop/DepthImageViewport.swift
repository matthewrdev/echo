import SwiftUI

struct DepthImageViewport: View {
    let data: DepthImagePresentation
    @AppStorage("depthOverlayOpacity") private var overlayOpacity = 1.0
    @StateObject private var controller = CanvasViewportController()

    var body: some View {
        VStack(spacing: 8) {
            HStack(spacing: 8) {
                CanvasControls(controller: controller, showsInstructions: false)
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
                renderer: DepthCanvasRenderer(data: data, overlayOpacity: overlayOpacity), controller: controller
            ).clipShape(RoundedRectangle(cornerRadius: 8))
            if data.photograph == nil {
                Text("Source photograph unavailable").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}
