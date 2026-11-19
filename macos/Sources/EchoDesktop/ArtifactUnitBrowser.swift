import SwiftUI

struct ArtifactUnitBrowser: View {
    let units: [ArtifactUnit]
    let selected: String
    let selection: (String) -> Void
    var footer: String?
    @State private var query = ""

    private var filtered: [ArtifactUnit] {
        query.isEmpty ? units : units.filter { $0.title.localizedCaseInsensitiveContains(query) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            TextField("Find photographs…", text: $query).textFieldStyle(.roundedBorder)
            if footer == nil || !query.isEmpty {
                Text("\(filtered.count) of \(units.count)").font(.caption).foregroundStyle(.secondary)
            }
            NativeArtifactUnitList(units: filtered, selected: selected, selection: selection)
            if let footer {
                Text(footer).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
            }
        }.frame(width: 190)
    }
}
