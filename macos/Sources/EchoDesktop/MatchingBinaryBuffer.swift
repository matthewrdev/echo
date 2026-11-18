import Foundation

struct MatchingBinaryBuffer {
    let data: Data

    init(header: URL, payload: String, kind: Int, bytes: Int) throws {
        guard !payload.isEmpty, URL(fileURLWithPath: payload).lastPathComponent == payload,
            payload != ".", payload != "..", bytes >= 0, bytes <= Int.max - 16
        else { throw CLIError(message: "Invalid matching payload path or size") }
        data = try Data(
            contentsOf: header.deletingLastPathComponent().appendingPathComponent(payload), options: .mappedIfSafe)
        guard data.count == bytes + 16, integer(0) == 0x3142_4d45,
            integer(4) == 1, integer(8) == kind
        else { throw CLIError(message: "Invalid matching binary header or length") }
    }

    func integer(_ offset: Int) -> Int {
        data.withUnsafeBytes { Int(Int32(littleEndian: $0.loadUnaligned(fromByteOffset: offset, as: Int32.self))) }
    }

    func double(_ offset: Int) -> Double {
        data.withUnsafeBytes {
            Double(bitPattern: UInt64(littleEndian: $0.loadUnaligned(fromByteOffset: offset, as: UInt64.self)))
        }
    }

    func pixel(_ offset: Int) -> [Double] { [double(offset), double(offset + 8)] }
}
