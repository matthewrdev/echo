import AppKit

@MainActor
final class ArtifactUnitListCoordinator: NSObject, NSTableViewDataSource, NSTableViewDelegate {
    private var units: [ArtifactUnit] = []
    private var selected = ""
    private var selection: ((String) -> Void)?
    private var updating = false

    func update(table: NSTableView, units: [ArtifactUnit], selected: String, selection: @escaping (String) -> Void) {
        let changedUnits = self.units != units
        let changedSelection = self.selected != selected
        let previousSelection = self.selected
        self.units = units
        self.selected = selected
        self.selection = selection
        updating = true
        defer { updating = false }

        if changedUnits { table.reloadData() }
        let row = units.firstIndex { $0.id == selected }
        if table.selectedRow != (row ?? -1) {
            table.selectRowIndexes(row.map { IndexSet(integer: $0) } ?? [], byExtendingSelection: false)
        }
        if changedSelection {
            for id in [previousSelection, selected] {
                guard let index = units.firstIndex(where: { $0.id == id }),
                    let cell = table.view(atColumn: 0, row: index, makeIfNecessary: false) as? ArtifactUnitTableCell
                else { continue }
                cell.configure(unit: units[index], selected: id == selected)
            }
            if let row { table.scrollRowToVisible(row) }
        }
    }

    func numberOfRows(in tableView: NSTableView) -> Int { units.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard units.indices.contains(row) else { return nil }
        let identifier = NSUserInterfaceItemIdentifier("artifact-unit")
        let cell =
            tableView.makeView(withIdentifier: identifier, owner: nil) as? ArtifactUnitTableCell
            ?? ArtifactUnitTableCell()
        cell.identifier = identifier
        cell.configure(unit: units[row], selected: units[row].id == selected)
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard !updating, let table = notification.object as? NSTableView,
            units.indices.contains(table.selectedRow)
        else { return }
        let id = units[table.selectedRow].id
        if id != selected { selection?(id) }
    }
}
