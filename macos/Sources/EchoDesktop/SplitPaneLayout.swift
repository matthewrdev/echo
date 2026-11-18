import Foundation

struct SplitPaneLayout: Equatable {
    let isVertical: Bool
    let paneIndex: Int
    let minimum: CGFloat
    let maximum: CGFloat
    let otherMinimum: CGFloat

    static let sidebar = SplitPaneLayout(
        isVertical: true, paneIndex: 0, minimum: 250, maximum: 500, otherMinimum: 740)
    static let activity = SplitPaneLayout(
        isVertical: false, paneIndex: 1, minimum: 100, maximum: .infinity, otherMinimum: 180)

    func lengthRange(available: CGFloat, collapsedLength: CGFloat?) -> ClosedRange<CGFloat> {
        if let collapsedLength {
            let length = min(collapsedLength, available)
            return length...length
        }
        let upper = max(0, min(maximum, available - otherMinimum))
        return min(minimum, upper)...upper
    }
}
