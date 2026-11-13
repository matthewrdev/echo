import SwiftUI

struct ArtifactUnitBrowser: View {
    let units: [ArtifactUnit]
    let selected: String
    let selection: (String) -> Void
    @State private var query = ""

    private var filtered: [ArtifactUnit] {
        units.filter { query.isEmpty || $0.title.localizedCaseInsensitiveContains(query) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            TextField("Find photographs…", text: $query).textFieldStyle(.roundedBorder)
            Text("\(filtered.count) of \(units.count)").font(.caption).foregroundStyle(.secondary)
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 6) {
                        ForEach(filtered) { unit in
                            Button {
                                selection(unit.id)
                            } label: {
                                ArtifactUnitRow(unit: unit, selected: selected == unit.id)
                            }.buttonStyle(.plain).id(unit.id)
                        }
                    }
                }
                .onChange(of: selected) { _, id in proxy.scrollTo(id) }
            }
        }.frame(width: 190)
    }
}
