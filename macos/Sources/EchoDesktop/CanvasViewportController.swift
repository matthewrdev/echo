import AppKit

@MainActor
final class CanvasViewportController: ObservableObject {
    weak var view: InteractiveCanvasView?
    @Published var zoom = 1.0
    @Published var hover: CanvasHover?

    func fit() { view?.fit() }
    func zoomIn() { view?.zoom(by: 1.4) }
    func zoomOut() { view?.zoom(by: 1 / 1.4) }
}
