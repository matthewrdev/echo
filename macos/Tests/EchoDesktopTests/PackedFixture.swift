import Foundation

enum PackedFixture {
    static func append<T: FixedWidthInteger>(_ value: T, to bytes: inout Data) {
        var littleEndian = value.littleEndian
        withUnsafeBytes(of: &littleEndian) { bytes.append(contentsOf: $0) }
    }

    static func append(_ value: String, to bytes: inout Data) {
        var remaining = value.utf8.count
        while remaining >= 128 {
            bytes.append(UInt8(remaining & 127) | 128)
            remaining >>= 7
        }
        bytes.append(UInt8(remaining))
        bytes.append(contentsOf: value.utf8)
    }

    static func header(_ magic: UInt32) -> Data {
        var bytes = Data()
        append(magic, to: &bytes)
        return bytes
    }

    static func frame(_ payload: Data) -> Data {
        var bytes = header(UInt32(payload.count))
        bytes.append(payload)
        return bytes
    }

    static func event(_ sequence: Int, type: String, fields: [String: String]) -> Data {
        var bytes = Data()
        append(UInt64(sequence), to: &bytes)
        append(UInt64(639_303_012_000_000_000), to: &bytes)
        append("test", to: &bytes)
        append(type, to: &bytes)
        append(UInt32(fields.count), to: &bytes)
        for (key, value) in fields.sorted(by: { $0.key < $1.key }) {
            append(key, to: &bytes)
            bytes.append(1)
            append(value, to: &bytes)
        }
        return frame(bytes)
    }

    static func imageIndex(_ file: URL, units: [String: String]) throws {
        var bytes = header(0x3155_4945)
        for (id, artifact) in units.sorted(by: { $0.key < $1.key }) {
            var payload = Data()
            append(id, to: &payload)
            append(artifact, to: &payload)
            bytes.append(frame(payload))
        }
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try bytes.write(to: file)
    }

    static func fusion(_ file: URL, count: Int, voxel: Double) throws {
        var bytes = header(0x3150_4645)
        append(UInt32(count), to: &bytes)
        append(voxel.bitPattern, to: &bytes)
        append(UInt32(count), to: &bytes)
        append(UInt32(count), to: &bytes)
        append(UInt32(count), to: &bytes)
        for index in 0..<count {
            append(UInt32(index % 8), to: &bytes)
            append(UInt32(index / 8), to: &bytes)
            append(UInt32(0), to: &bytes)
            append(Float(0).bitPattern, to: &bytes)
            append(UInt32(3), to: &bytes)
        }
        try bytes.write(to: file)
    }

    static func emptyCloud(_ file: URL, count: UInt32) throws {
        var bytes = header(0x3144_4345)
        append(count, to: &bytes)
        append(count, to: &bytes)
        append(UInt32(0), to: &bytes)
        append(UInt32(0), to: &bytes)
        try bytes.write(to: file)
    }
}
