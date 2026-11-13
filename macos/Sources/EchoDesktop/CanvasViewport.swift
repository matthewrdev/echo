import SwiftUI

struct CanvasViewport: NSViewRepresentable {
    let renderer: CanvasRenderer
    @ObservedObject var controller: CanvasViewportController

    func makeNSView(context: Context) -> InteractiveCanvasView {
        let view = InteractiveCanvasView()
        controller.view = view
        view.zoomChanged = { value in
            DispatchQueue.main.async { controller.zoom = value }
        }
        view.wantsLayer = true
        view.layer?.masksToBounds = true
        return view
    }

    func sizeThatFits(_ proposal: ProposedViewSize, nsView: InteractiveCanvasView, context: Context) -> CGSize? {
        CGSize(width: proposal.width ?? 400, height: proposal.height ?? 300)
    }

    func updateNSView(_ view: InteractiveCanvasView, context: Context) {
        view.renderer = renderer
    }
}
