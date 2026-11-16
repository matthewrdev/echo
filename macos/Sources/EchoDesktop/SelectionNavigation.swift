import SwiftUI

struct SelectionNavigation {
    func destination<ID: Equatable>(in rows: [[ID]], from selected: ID?, key: KeyEquivalent) -> ID? {
        let rows = rows.filter { !$0.isEmpty }
        guard [.upArrow, .downArrow, .leftArrow, .rightArrow].contains(key), let first = rows.first?.first else {
            return nil
        }
        guard let row = rows.firstIndex(where: { $0.contains(where: { $0 == selected }) }),
            let column = rows[row].firstIndex(where: { $0 == selected })
        else { return first }

        switch key {
        case .upArrow:
            guard row > 0 else { return selected }
            return rows[row - 1][min(column, rows[row - 1].count - 1)]
        case .downArrow:
            guard row + 1 < rows.count else { return selected }
            return rows[row + 1][min(column, rows[row + 1].count - 1)]
        case .leftArrow:
            if column > 0 { return rows[row][column - 1] }
            return row > 0 ? rows[row - 1].last : selected
        case .rightArrow:
            if column + 1 < rows[row].count { return rows[row][column + 1] }
            return row + 1 < rows.count ? rows[row + 1].first : selected
        default:
            return nil
        }
    }
}
