import SwiftUI

struct SceneOrientationControls: View {
    let controller: SceneViewportController
    let metricScale: Bool
    @State private var showFrame = false

    var body: some View {
        HStack(spacing: 4) {
            Menu {
                Button(metricScale ? "Top · north up" : "Top · −Z up") { controller.orient(.top) }
                Button(metricScale ? "Look north · level" : "Front · Y up") { controller.orient(.north) }
                Button(metricScale ? "Look east · level" : "Side · Y up") { controller.orient(.east) }
            } label: {
                Label("Standard views", systemImage: "cube")
            }
            .menuStyle(.borderlessButton).menuIndicator(.hidden).help(
                "Top, front and side views in the saved coordinate frame"
            )
            .labelStyle(.iconOnly).modifier(ViewerIconFrame()).accessibilityLabel("Standard views")
            Button("Coordinate frame", systemImage: "info.circle") { showFrame.toggle() }
                .help("Coordinate frame and scale")
                .popover(isPresented: $showFrame) {
                    Text(
                        metricScale
                            ? "GPS frame: X east, Y up, Z south. Coordinates are in metres relative to the capture origin. Orientation follows capture priors and has not been survey validated. The grid is a level reference, not measured terrain."
                            : "Local frame: Y up. Geographic orientation and metric scale are unavailable. The grid uses scene units."
                    )
                    .frame(width: 300).padding(16)
                }
        }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).fixedSize()
    }
}
