import AppKit
import SwiftUI

struct NativeArtifactUnitList: NSViewRepresentable {
    let units: [ArtifactUnit]
    let selected: String
    let selection: (String) -> Void

    func makeCoordinator() -> ArtifactUnitListCoordinator { ArtifactUnitListCoordinator() }

    func makeNSView(context: Context) -> NSScrollView {
        let table = NSTableView()
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("artifact"))
        column.resizingMask = .autoresizingMask
        table.addTableColumn(column)
        table.columnAutoresizingStyle = .lastColumnOnlyAutoresizingStyle
        table.headerView = nil
        table.rowHeight = 148
        table.usesAutomaticRowHeights = false
        table.intercellSpacing = NSSize(width: 0, height: 6)
        table.backgroundColor = .clear
        table.selectionHighlightStyle = .none
        table.allowsMultipleSelection = false
        table.dataSource = context.coordinator
        table.delegate = context.coordinator
        table.setAccessibilityLabel("Artifact photographs")
        let scroll = NSScrollView()
        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = false
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let table = scroll.documentView as? NSTableView else { return }
        context.coordinator.update(table: table, units: units, selected: selected, selection: selection)
    }
}
