import SceneKit
import SwiftUI

struct SceneOrientationView: View {
    @ObservedObject var orientation: SceneOrientationState
    let metricScale: Bool

    var body: some View {
        Canvas { context, size in
            guard let frame = orientation.frame else { return }
            let centre = CGPoint(x: size.width / 2, y: size.height / 2)
            axis(
                context: &context, centre: centre, direction: frame.east,
                label: metricScale ? "E" : "X", color: .red)
            axis(
                context: &context, centre: centre, direction: frame.up,
                label: metricScale ? "Up" : "Y", color: .green)
            axis(
                context: &context, centre: centre, direction: frame.north,
                label: metricScale ? "N" : "−Z", color: .blue)
        }.frame(width: 98, height: 78)
            .allowsHitTesting(false)
            .accessibilityLabel(
                metricScale ? "Orientation: east, north and world up" : "Orientation: local X, Y and negative Z")
    }

    private func axis(
        context: inout GraphicsContext, centre: CGPoint, direction: SIMD3<Float>, label: String,
        color: Color
    ) {
        let end = CGPoint(x: centre.x + CGFloat(direction.x) * 24, y: centre.y - CGFloat(direction.y) * 24)
        var line = Path()
        line.move(to: centre)
        line.addLine(to: end)
        context.stroke(line, with: .color(color), lineWidth: 2)
        context.fill(Path(ellipseIn: CGRect(x: end.x - 3, y: end.y - 3, width: 6, height: 6)), with: .color(color))
        context.draw(
            Text(label).font(.caption.bold()).foregroundColor(color),
            at: CGPoint(
                x: end.x + CGFloat(direction.x) * 10,
                y: end.y - CGFloat(direction.y) * 10 - (abs(direction.z) > 0.95 ? 10 : 0)))
    }
}
