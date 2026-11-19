import AppKit
import SwiftUI

@MainActor
final class ArtifactUnitTableCell: NSTableCellView {
    private var host: NSHostingView<AnyView>?

    func configure(unit: ArtifactUnit, selected: Bool) {
        let content = AnyView(ArtifactUnitRow(unit: unit, selected: selected).id(unit.id))
        if let host {
            host.rootView = content
        } else {
            let host = NSHostingView(rootView: content)
            host.translatesAutoresizingMaskIntoConstraints = false
            addSubview(host)
            NSLayoutConstraint.activate([
                host.leadingAnchor.constraint(equalTo: leadingAnchor),
                host.trailingAnchor.constraint(equalTo: trailingAnchor),
                host.topAnchor.constraint(equalTo: topAnchor),
                host.bottomAnchor.constraint(equalTo: bottomAnchor),
            ])
            self.host = host
        }
        setAccessibilityLabel(unit.title + ", " + unit.detail)
    }
}
