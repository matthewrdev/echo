import SwiftUI

struct MetadataValueRow: View {
    let entry: MetadataEntry
    let copy: (String) -> Void
    var loadPayload: ((String) async throws -> Data)?
    @State private var copied = false

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(alignment: .top) {
                Text(entry.name).font(.caption.weight(.semibold)).textSelection(.enabled)
                Spacer(minLength: 8)
                Button {
                    copy(entry.value)
                    copied = true
                } label: {
                    Label(copied ? "Copied" : "Copy value", systemImage: copied ? "checkmark" : "doc.on.doc")
                }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).help("Copy \(entry.key) value")
            }
            if entry.isOpaque {
                if let loadPayload {
                    MetadataPayloadView(tag: entry.key, load: loadPayload)
                } else {
                    Text(entry.summary).font(.caption).foregroundStyle(.secondary)
                }
                if !entry.isBinary {
                    DisclosureGroup("Raw reader output") {
                        ScrollView {
                            Text(entry.value).font(.system(.caption2, design: .monospaced))
                                .textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                        }.frame(height: 100)
                    }.font(.caption)
                }
            } else if entry.value.count > 320 {
                Text(String(entry.value.prefix(160)) + "…").font(.callout).textSelection(.enabled)
                DisclosureGroup("Full value") {
                    ScrollView { Text(entry.value).font(.callout).textSelection(.enabled) }.frame(height: 120)
                }.font(.caption)
            } else {
                Text(entry.value).font(.callout).textSelection(.enabled)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .contextMenu {
            Button("Copy value") { copy(entry.value) }
            Button("Copy tag and value") { copy("\(entry.key): \(entry.value)") }
        }
        .onChange(of: entry.value) { _, _ in copied = false }
    }
}
