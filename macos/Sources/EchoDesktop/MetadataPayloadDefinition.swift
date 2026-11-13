import Foundation

struct MetadataPayloadDefinition {
    let meaning: String?
    let formatDescription: String
    let reference: URL?

    init(tag: String) {
        let components = tag.split(separator: ":", maxSplits: 1)
        let name = components.last.map(String.init) ?? tag
        let isDJI = components.first == "DJI"
        let meanings = [
            "AEHistogramInfo": "Auto-exposure histogram",
            "AELocalHistogram": "Auto-exposure local histogram",
            "AELiveViewHistogramInfo": "Live-view auto-exposure histogram",
            "AELiveViewLocalHistogram": "Live-view auto-exposure local histogram",
            "Histogram": "Camera histogram",
            "AEDebugInfo": "Auto-exposure diagnostics",
            "AFDebugInfo": "Autofocus diagnostics",
            "AWBDebugInfo": "Auto white-balance diagnostics",
        ]
        meaning = isDJI ? meanings[name] : nil
        // ExifTool DJI::Info names these fields, but ProcessDJIInfo returns their
        // payloads as opaque bytes. It does not specify bin type, endianness or layout.
        // https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/DJI.pm
        formatDescription =
            meaning == nil
            ? "Binary payload · no field decoder available"
            : "Binary payload · sample type and layout are not defined by ExifTool"
        reference =
            meaning == nil
            ? nil : URL(string: "https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/DJI.pm")
    }
}
