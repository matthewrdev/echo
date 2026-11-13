import Foundation

enum MetadataSampleFormat: String, CaseIterable {
    case byte = "UInt8"
    case uint16LE = "UInt16 · little endian"
    case uint16BE = "UInt16 · big endian"
    case uint32LE = "UInt32 · little endian"
    case uint32BE = "UInt32 · big endian"
    case float32LE = "Float32 · little endian"
    case float32BE = "Float32 · big endian"

    var stride: Int {
        switch self {
        case .byte: return 1
        case .uint16LE, .uint16BE: return 2
        default: return 4
        }
    }

    func samples(_ data: Data, byteOffset: Int, limit: Int = 2048) -> [Double] {
        guard byteOffset >= 0, byteOffset < data.count else { return [] }
        let count = min(limit, (data.count - byteOffset) / stride)
        return data.withUnsafeBytes { buffer in
            (0..<count).map { index in
                let offset = byteOffset + index * stride
                switch self {
                case .byte: return Double(buffer[offset])
                case .uint16LE:
                    return Double(UInt16(littleEndian: buffer.loadUnaligned(fromByteOffset: offset, as: UInt16.self)))
                case .uint16BE:
                    return Double(UInt16(bigEndian: buffer.loadUnaligned(fromByteOffset: offset, as: UInt16.self)))
                case .uint32LE:
                    return Double(UInt32(littleEndian: buffer.loadUnaligned(fromByteOffset: offset, as: UInt32.self)))
                case .uint32BE:
                    return Double(UInt32(bigEndian: buffer.loadUnaligned(fromByteOffset: offset, as: UInt32.self)))
                case .float32LE:
                    return Double(
                        Float(
                            bitPattern: UInt32(
                                littleEndian: buffer.loadUnaligned(fromByteOffset: offset, as: UInt32.self))))
                case .float32BE:
                    return Double(
                        Float(
                            bitPattern: UInt32(bigEndian: buffer.loadUnaligned(fromByteOffset: offset, as: UInt32.self))
                        ))
                }
            }
        }
    }
}
