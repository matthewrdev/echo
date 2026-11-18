import AppKit
import SwiftUI

struct PersistentSplitView<First: View, Second: View>: NSViewRepresentable {
    let layout: SplitPaneLayout
    @Binding var paneLength: Double
    var collapsedLength: CGFloat? = nil
    @ViewBuilder var first: () -> First
    @ViewBuilder var second: () -> Second

    func makeCoordinator() -> PersistentSplitCoordinator {
        PersistentSplitCoordinator(layout: layout, paneLength: paneLength, collapsedLength: collapsedLength)
    }

    func makeNSView(context: Context) -> NSSplitView {
        let split = NSSplitView()
        split.isVertical = layout.isVertical
        split.dividerStyle = .thin
        let firstHost = NSHostingView(rootView: AnyView(first().environment(\.self, context.environment)))
        let secondHost = NSHostingView(rootView: AnyView(second().environment(\.self, context.environment)))
        firstHost.sizingOptions = []
        secondHost.sizingOptions = []
        split.addArrangedSubview(firstHost)
        split.addArrangedSubview(secondHost)
        split.delegate = context.coordinator
        return split
    }

    func updateNSView(_ split: NSSplitView, context: Context) {
        (split.arrangedSubviews[0] as? NSHostingView<AnyView>)?.rootView = AnyView(
            first().environment(\.self, context.environment))
        (split.arrangedSubviews[1] as? NSHostingView<AnyView>)?.rootView = AnyView(
            second().environment(\.self, context.environment))
        context.coordinator.onResize = { paneLength = $0 }
        context.coordinator.update(split, paneLength: paneLength, collapsedLength: collapsedLength)
    }
}
