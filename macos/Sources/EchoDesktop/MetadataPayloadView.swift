import SwiftUI

struct MetadataPayloadView: View {
    let tag: String
    let load: (String) async throws -> Data
    @State private var data: Data?
    @State private var error: String?
    @State private var format = MetadataSampleFormat.byte
    @State private var page = 0
    @State private var mode = "Bytes"
    private var definition: MetadataPayloadDefinition { MetadataPayloadDefinition(tag: tag) }

    private var pageSize: Int { mode == "Plot" ? 2048 * format.stride : 256 }
    private var byteOffset: Int { page * pageSize }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if let data {
                if let meaning = definition.meaning { Text(meaning).fontWeight(.medium) }
                Text("\(data.count.formatted()) original bytes").font(.caption).foregroundStyle(.secondary)
                if mode != "Image" {
                    HStack {
                        Text(definition.formatDescription).foregroundStyle(.secondary)
                        if let reference = definition.reference { Link("Definition", destination: reference) }
                    }
                }
                Picker("View", selection: $mode) {
                    Text("Bytes").tag("Bytes")
                    Text("Raw plot").tag("Plot")
                    Text("Text").tag("Text")
                    if NSImage(data: data) != nil { Text("Image").tag("Image") }
                }.pickerStyle(.segmented)
                if mode == "Image", let image = NSImage(data: data) {
                    Image(nsImage: image).resizable().scaledToFit().frame(maxHeight: 200)
                } else {
                    HStack {
                        Button("Previous page", systemImage: "chevron.left") { page -= 1 }.disabled(page == 0)
                        Text(
                            "Bytes \(byteOffset)–\(min(data.count, byteOffset + pageSize) - 1)"
                        )
                        .monospacedDigit()
                        Button("Next page", systemImage: "chevron.right") { page += 1 }.disabled(
                            (page + 1) * pageSize >= data.count)
                        Spacer()
                    }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain)
                    if mode == "Plot" {
                        Picker("Interpret bytes as", selection: $format) {
                            ForEach(MetadataSampleFormat.allCases, id: \.self) { Text($0.rawValue) }
                        }
                        .help("Raw numeric interpretation. The vendor’s bin layout and units are not decoded.")
                        MetadataSamplePlot(
                            values: format.samples(data, byteOffset: byteOffset),
                            sampleOffset: byteOffset / format.stride
                        )
                        .frame(height: 140)
                    } else {
                        ScrollView([.horizontal, .vertical]) {
                            Text(mode == "Bytes" ? hex(data) : text(data))
                                .font(.system(.caption2, design: .monospaced)).textSelection(.enabled)
                                .frame(maxWidth: .infinity, alignment: .topLeading)
                        }.frame(height: 160)
                    }
                }
            } else if let error {
                Text(error).foregroundStyle(.red)
            } else {
                ProgressView("Reading original field…")
            }
        }.font(.caption).controlSize(.small)
            .onChange(of: mode) { _, _ in page = 0 }
            .onChange(of: format) { _, _ in page = 0 }
            .task(id: tag) {
                guard data == nil else { return }
                do {
                    let loaded = try await load(tag)
                    guard !Task.isCancelled else { return }
                    data = loaded
                    if NSImage(data: loaded) != nil { mode = "Image" }
                } catch {
                    guard !Task.isCancelled else { return }
                    self.error = error.localizedDescription
                }
            }
    }

    private func hex(_ data: Data) -> String {
        let start = byteOffset
        let end = min(data.count, start + 256)
        return stride(from: start, to: end, by: 16).map { offset in
            let row = data[offset..<min(end, offset + 16)]
            let bytes = row.map { String(format: "%02X", $0) }.joined(separator: " ")
            let ascii = String(row.map { $0 >= 32 && $0 <= 126 ? Character(UnicodeScalar($0)) : "." })
            return String(format: "%08X  ", offset) + bytes.padding(toLength: 47, withPad: " ", startingAt: 0) + "  "
                + ascii
        }.joined(separator: "\n")
    }

    private func text(_ data: Data) -> String {
        let bytes = data[(byteOffset)..<min(data.count, byteOffset + 256)]
        return String(data: bytes, encoding: .utf8) ?? "This page is not valid UTF-8 text. Use the byte viewer."
    }
}
