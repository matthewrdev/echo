import SwiftUI

struct ArtifactUnitBrowser: View {
    let units: [ArtifactUnit]
    let selected: String
    let selection: (String) -> Void
    var footer: String?
    @State private var query = ""
    @FocusState private var browserFocused: Bool

    private var filtered: [ArtifactUnit] {
        units.filter { query.isEmpty || $0.title.localizedCaseInsensitiveContains(query) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            TextField("Find photographs…", text: $query).textFieldStyle(.roundedBorder)
            if footer == nil || !query.isEmpty {
                Text("\(filtered.count) of \(units.count)").font(.caption).foregroundStyle(.secondary)
            }
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 6) {
                        ForEach(filtered) { unit in
                            Button {
                                browserFocused = true
                                selection(unit.id)
                            } label: {
                                ArtifactUnitRow(unit: unit, selected: selected == unit.id)
                            }.buttonStyle(.plain).id(unit.id)
                        }
                    }
                }
                .focusable()
                .focused($browserFocused)
                .onKeyPress(keys: [.upArrow, .downArrow]) { press in
                    guard press.modifiers.intersection([.command, .control, .option, .shift]).isEmpty,
                        let id = SelectionNavigation().destination(
                            in: filtered.map { [$0.id] }, from: selected, key: press.key)
                    else { return .ignored }
                    if id != selected { selection(id) }
                    return .handled
                }
                .onChange(of: selected) { _, id in proxy.scrollTo(id) }
            }
            if let footer {
                Text(footer).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
            }
        }.frame(width: 190)
    }
}
