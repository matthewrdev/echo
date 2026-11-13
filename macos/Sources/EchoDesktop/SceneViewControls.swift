import SwiftUI

struct SceneViewControls: View {
    @ObservedObject var controller: SceneViewportController
    @Binding var showGround: Bool
    let metricScale: Bool

    var body: some View {
        VStack(spacing: 4) {
            Button("Reset view", systemImage: "arrow.counterclockwise") { controller.reset() }
                .help("Fit the cloud or mesh with world up vertical")
            Button("Ground grid", systemImage: "grid") { showGround.toggle() }
                .labelStyle(ViewerIconLabelStyle(selected: showGround))
                .accessibilityValue(showGround ? "On" : "Off")
                .help(
                    metricScale
                        ? "Ground grid: 1 m cells, 10 m guides" : "Ground grid: 1 unit cells; metric scale unavailable")
        }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).fixedSize()
    }
}
