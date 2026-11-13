import Foundation

struct MetadataEntry: Identifiable {
    let key: String
    let value: String
    var id: String { key }
    var group: String {
        key.split(separator: ":", maxSplits: 1).count > 1 ? String(key.split(separator: ":")[0]) : "Imported"
    }
    var name: String { key.split(separator: ":", maxSplits: 1).last.map(String.init) ?? key }
    var isBinary: Bool { value.hasPrefix("(Binary data ") }
    var isOpaque: Bool {
        isBinary || name.contains("_0x")
            || value.unicodeScalars.contains { $0.value < 32 && ![9, 10, 13].contains($0.value) }
    }
    var summary: String {
        if isBinary { return value.replacingOccurrences(of: ", use -b option to extract", with: "") }
        if isOpaque {
            return "Undecoded vendor payload · \(value.utf8.count) text bytes returned by the metadata reader"
        }
        return value
    }
}
