import AppKit

protocol CanvasRenderer: AnyObject {
    var identity: String { get }
    var size: CGSize { get }
    func draw(in context: CGContext, scale: CGFloat)
    func select(at point: CGPoint, tolerance: CGFloat)
}
