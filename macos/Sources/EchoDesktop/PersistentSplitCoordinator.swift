import AppKit

@MainActor
final class PersistentSplitCoordinator: NSObject, NSSplitViewDelegate {
    var onResize: (Double) -> Void = { _ in }
    private let layout: SplitPaneLayout
    private var preferredLength: CGFloat
    private var bindingLength: Double
    private var collapsedLength: CGFloat?
    private var applyingLayout = false

    init(layout: SplitPaneLayout, paneLength: Double, collapsedLength: CGFloat? = nil) {
        self.layout = layout
        self.preferredLength = paneLength
        self.bindingLength = paneLength
        self.collapsedLength = collapsedLength
    }

    func update(_ split: NSSplitView, paneLength: Double, collapsedLength: CGFloat?) {
        let lengthChanged = paneLength != bindingLength
        let collapseChanged = collapsedLength != self.collapsedLength
        if lengthChanged {
            bindingLength = paneLength
            preferredLength = paneLength
        }
        self.collapsedLength = collapsedLength
        if lengthChanged || collapseChanged { arrange(split) }
    }

    func splitView(_ splitView: NSSplitView, resizeSubviewsWithOldSize oldSize: NSSize) {
        arrange(splitView)
    }

    func splitView(
        _ splitView: NSSplitView, constrainMinCoordinate proposedMinimumPosition: CGFloat, ofSubviewAt dividerIndex: Int
    ) -> CGFloat {
        let available = availableLength(splitView)
        let range = layout.lengthRange(available: available, collapsedLength: collapsedLength)
        let minimum = layout.paneIndex == 0 ? range.lowerBound : available - range.upperBound
        return max(proposedMinimumPosition, minimum)
    }

    func splitView(
        _ splitView: NSSplitView, constrainMaxCoordinate proposedMaximumPosition: CGFloat, ofSubviewAt dividerIndex: Int
    ) -> CGFloat {
        let available = availableLength(splitView)
        let range = layout.lengthRange(available: available, collapsedLength: collapsedLength)
        let maximum = layout.paneIndex == 0 ? range.upperBound : available - range.lowerBound
        return min(proposedMaximumPosition, maximum)
    }

    func splitViewDidResizeSubviews(_ notification: Notification) {
        guard !applyingLayout, collapsedLength == nil else { return }
        guard let split = notification.object as? NSSplitView, split.arrangedSubviews.count == 2 else { return }
        let pane = split.arrangedSubviews[layout.paneIndex]
        let length = layout.isVertical ? pane.frame.width : pane.frame.height
        let expected = displayedLength(available: availableLength(split))
        guard length > 0, abs(length - expected) > 0.5 else { return }
        preferredLength = length
        // AppKit can notify while SwiftUI is laying out. Publish the preference on the next turn.
        DispatchQueue.main.async { [weak self] in self?.onResize(length) }
    }

    private func arrange(_ split: NSSplitView) {
        guard split.arrangedSubviews.count == 2 else { return }
        applyingLayout = true
        defer { applyingLayout = false }
        let available = availableLength(split)
        let length = displayedLength(available: available)
        let firstLength = layout.paneIndex == 0 ? length : available - length
        split.adjustSubviews()
        split.setPosition(firstLength, ofDividerAt: 0)
    }

    private func availableLength(_ split: NSSplitView) -> CGFloat {
        max(0, (layout.isVertical ? split.bounds.width : split.bounds.height) - split.dividerThickness)
    }

    private func displayedLength(available: CGFloat) -> CGFloat {
        let range = layout.lengthRange(available: available, collapsedLength: collapsedLength)
        let length = preferredLength.isFinite ? preferredLength : range.lowerBound
        return min(max(length, range.lowerBound), range.upperBound)
    }
}
