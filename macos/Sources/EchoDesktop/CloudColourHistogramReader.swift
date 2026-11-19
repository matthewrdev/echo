import Foundation
import simd

struct CloudColourHistogramReader: Sendable {
    func read(
        file: URL, expectedCount: Int, regions: [ExclusionRegion], minimumSupport: Double,
        progress: @Sendable (Double) -> Void = { _ in }
    ) throws -> ColourFrequencies {
        try Task.checkCancellation()
        let handle = try FileHandle(forReadingFrom: file)
        defer { try? handle.close() }
        let size = try handle.seekToEnd()
        guard size <= UInt64(Int.max) else { throw CLIError(message: "Cloud file is too large.") }
        try handle.seek(toOffset: 0)
        let layout = try DenseCloudLayout(
            prefix: handle.read(upToCount: 8192) ?? Data(), expectedCount: expectedCount, fileSize: Int(size))
        try handle.seek(toOffset: UInt64(layout.offset))
        var bins = [UInt32](repeating: 0, count: 1 << 24)
        let crop = RegionCropFilter(regions)
        let hasCrop = regions.contains { $0.enabled && $0.isValid }
        var scanned = 0
        var included = 0
        while scanned < expectedCount {
            try Task.checkCancellation()
            let count = min(65536, expectedCount - scanned)
            let needed = count * DenseCloudLayout.stride
            var chunk = Data()
            while chunk.count < needed {
                try Task.checkCancellation()
                guard let part = try handle.read(upToCount: needed - chunk.count), !part.isEmpty else {
                    throw CLIError(message: "Dense cloud ended before its saved point count.")
                }
                chunk.append(part)
            }
            try chunk.withUnsafeBytes { bytes in
                for index in 0..<count {
                    if index.isMultiple(of: 4096) { try Task.checkCancellation() }
                    let offset = index * DenseCloudLayout.stride
                    let confidence = number(bytes, at: offset + 19)
                    guard confidence.isFinite, confidence >= Float(minimumSupport) else { continue }
                    let point = SIMD3(
                        Double(number(bytes, at: offset)), Double(number(bytes, at: offset + 4)),
                        Double(number(bytes, at: offset + 8)))
                    guard point.x.isFinite, point.y.isFinite, point.z.isFinite else { continue }
                    if hasCrop && crop.isExcluded(point) { continue }
                    let rgb = Int(bytes[offset + 12]) << 16 | Int(bytes[offset + 13]) << 8 | Int(bytes[offset + 14])
                    bins[rgb] += 1
                    included += 1
                }
            }
            scanned += count
            if scanned.isMultiple(of: 1_048_576) || scanned == expectedCount {
                progress(Double(scanned) / Double(max(1, expectedCount)))
            }
        }
        return ColourFrequencies(bins: bins, scanned: scanned, included: included)
    }

    private func number(_ bytes: UnsafeRawBufferPointer, at offset: Int) -> Float {
        Float(bitPattern: UInt32(littleEndian: bytes.loadUnaligned(fromByteOffset: offset, as: UInt32.self)))
    }
}
