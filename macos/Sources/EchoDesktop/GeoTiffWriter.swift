import AppKit

struct GeoTiffWriter {
    func encode(_ bitmap: NSBitmapImageRep, worldFile: [Double]) throws -> Data {
        guard var data = bitmap.representation(using: .tiff, properties: [:]), data.count >= 8,
            worldFile.count == 6, worldFile.allSatisfy(\.isFinite),
            worldFile[0] > 0, worldFile[3] < 0, worldFile[1] == 0, worldFile[2] == 0
        else { throw CLIError(message: "Cannot encode the geographic TIFF image.") }
        let little = data[0] == 0x49 && data[1] == 0x49
        guard little || (data[0] == 0x4d && data[1] == 0x4d), read(data, 2, 2, little) == 42 else {
            throw CLIError(message: "Unsupported TIFF encoding.")
        }
        let original = Int(read(data, 4, 4, little))
        guard original >= 8, original + 2 <= data.count else {
            throw CLIError(message: "Invalid TIFF directory.")
        }
        let count = Int(read(data, original, 2, little))
        guard original + 2 + count * 12 + 4 <= data.count else {
            throw CLIError(message: "Incomplete TIFF directory.")
        }
        var entries: [TiffEntry] = []
        for index in 0..<count {
            let offset = original + 2 + index * 12
            let tag = UInt16(read(data, offset, 2, little))
            if [33550, 33922, 34735, 34736, 34737, 34264].contains(tag) { continue }
            entries.append(
                TiffEntry(
                    tag: tag, type: UInt16(read(data, offset + 2, 2, little)),
                    count: read(data, offset + 4, 4, little), value: read(data, offset + 8, 4, little)))
        }
        while data.count % 8 != 0 { data.append(0) }
        let scaleOffset = UInt32(data.count)
        appendDoubles([worldFile[0], -worldFile[3], 0], to: &data, little: little)
        let tieOffset = UInt32(data.count)
        // RasterPixelIsArea uses the upper-left corner; world files use pixel centres.
        appendDoubles(
            [
                0, 0, 0, worldFile[4] - worldFile[0] / 2, worldFile[5] - worldFile[3] / 2, 0,
            ], to: &data, little: little)
        let keyOffset = UInt32(data.count)
        let keys: [UInt16] = [
            1, 1, 1, 4,  // GeoTIFF 1.1; four keys.
            1024, 0, 1, 1,  // Projected model.
            1025, 0, 1, 1,  // RasterPixelIsArea.
            3072, 0, 1, 3857,  // WGS84 / Pseudo-Mercator.
            3076, 0, 1, 9001,  // Metres.
        ]
        for key in keys { append(UInt64(key), bytes: 2, to: &data, little: little) }
        entries.append(TiffEntry(tag: 33550, type: 12, count: 3, value: scaleOffset))
        entries.append(TiffEntry(tag: 33922, type: 12, count: 6, value: tieOffset))
        entries.append(TiffEntry(tag: 34735, type: 3, count: UInt32(keys.count), value: keyOffset))
        let directoryOffset = UInt32(data.count)
        append(UInt64(entries.count), bytes: 2, to: &data, little: little)
        for entry in entries.sorted(by: { $0.tag < $1.tag }) {
            append(UInt64(entry.tag), bytes: 2, to: &data, little: little)
            append(UInt64(entry.type), bytes: 2, to: &data, little: little)
            append(UInt64(entry.count), bytes: 4, to: &data, little: little)
            append(UInt64(entry.value), bytes: 4, to: &data, little: little)
        }
        append(0, bytes: 4, to: &data, little: little)
        var pointer = Data()
        append(UInt64(directoryOffset), bytes: 4, to: &pointer, little: little)
        data.replaceSubrange(4..<8, with: pointer)
        return data
    }

    private func read(_ data: Data, _ offset: Int, _ bytes: Int, _ little: Bool) -> UInt32 {
        var value: UInt32 = 0
        for index in 0..<bytes {
            value |= UInt32(data[offset + index]) << ((little ? index : bytes - index - 1) * 8)
        }
        return value
    }

    private func append(_ value: UInt64, bytes: Int, to data: inout Data, little: Bool) {
        for index in 0..<bytes {
            data.append(UInt8(truncatingIfNeeded: value >> ((little ? index : bytes - index - 1) * 8)))
        }
    }

    private func appendDoubles(_ values: [Double], to data: inout Data, little: Bool) {
        for value in values { append(value.bitPattern, bytes: 8, to: &data, little: little) }
    }
}
