import SwiftUI

struct ImageViewport: View {
    let image: NSImage
    @StateObject private var controller = CanvasViewportController()

    var body: some View {
        VStack(spacing: 8) {
            CanvasControls(controller: controller)
            CanvasViewport(renderer: ImageCanvasRenderer(image: image), controller: controller)
                .clipShape(RoundedRectangle(cornerRadius: 8))
        }
    }
}
