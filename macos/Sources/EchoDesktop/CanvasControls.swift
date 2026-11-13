import SwiftUI

struct CanvasControls: View {
    @ObservedObject var controller: CanvasViewportController
    var showsInstructions = true
    var body: some View {
        HStack(spacing: 10) {
            Button("Zoom out", systemImage: "minus.magnifyingglass") { controller.zoomOut() }
                .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain)
            Text("\(Int(controller.zoom * 100))%").monospacedDigit().frame(width: 48)
            Button("Zoom in", systemImage: "plus.magnifyingglass") { controller.zoomIn() }
                .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain)
            Button("Fit") { controller.fit() }
            Spacer()
            if showsInstructions {
                Text("Scroll to zoom · click and drag to pan").font(.caption).foregroundStyle(.secondary)
            }
        }
        .controlSize(.small)
    }
}
