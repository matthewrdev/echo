import SwiftUI

struct PointerTooltipLayout: Layout {
    let point: CGPoint

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        proposal.replacingUnspecifiedDimensions()
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        guard let tooltip = subviews.first else { return }
        let size = tooltip.sizeThatFits(.unspecified)
        let origin = tooltipOrigin(size: size, bounds: bounds)
        tooltip.place(at: origin, anchor: .topLeading, proposal: ProposedViewSize(size))
    }

    func tooltipOrigin(size: CGSize, bounds: CGRect) -> CGPoint {
        let x = bounds.minX + point.x
        let y = bounds.minY + point.y
        let right = x + 14
        let below = y + 18
        return CGPoint(
            x: max(
                bounds.minX + 4,
                min(right + size.width <= bounds.maxX - 4 ? right : x - size.width - 14, bounds.maxX - size.width - 4)),
            y: max(
                bounds.minY + 4,
                min(
                    below + size.height <= bounds.maxY - 4 ? below : y - size.height - 14, bounds.maxY - size.height - 4
                )))
    }
}
