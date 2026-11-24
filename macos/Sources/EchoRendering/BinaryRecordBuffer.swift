import Foundation

package struct BinaryRecordBuffer {
    package let data: Data
    package let size: Int
    package let count: Int
    private let recordsOffset: Int

    package init(file: URL, size: Int) throws {
        try self.init(data: Data(contentsOf: file, options: .mappedIfSafe), size: size)
    }

    package init(data: Data, size: Int, headerOffset: Int = 0) throws {
        self.data = data
        self.size = size
        guard size > 0, headerOffset >= 0, data.count >= 16, headerOffset <= data.count - 16 else {
            throw ModelRenderError(message: "Truncated binary artifact")
        }
        recordsOffset = headerOffset + 16
        let header = data.withUnsafeBytes { raw in
            (0..<4).map {
                Int32(littleEndian: raw.loadUnaligned(fromByteOffset: headerOffset + $0 * 4, as: Int32.self))
            }
        }
        count = Int(header[3])
        guard header[0] == 0x3150_4345, header[1] == 1, header[2] == size,
            count >= 0, count <= (data.count - recordsOffset) / size
        else { throw ModelRenderError(message: "Invalid binary artifact header or size") }
    }

    package func integer(_ record: Int, _ offset: Int = 0) -> Int {
        data.withUnsafeBytes { raw in
            Int(
                Int32(
                    littleEndian: raw.loadUnaligned(
                        fromByteOffset: recordsOffset + record * size + offset, as: Int32.self)))
        }
    }

    package func byte(_ record: Int, _ offset: Int) -> UInt8 {
        data[recordsOffset + record * size + offset]
    }

    package func float(_ record: Int, _ offset: Int = 0) -> Float {
        data.withUnsafeBytes { raw in
            Float(
                bitPattern: UInt32(
                    littleEndian: raw.loadUnaligned(
                        fromByteOffset: recordsOffset + record * size + offset, as: UInt32.self)))
        }
    }

    package func double(_ record: Int, _ offset: Int = 0) -> Double {
        data.withUnsafeBytes { raw in
            Double(
                bitPattern: UInt64(
                    littleEndian: raw.loadUnaligned(
                        fromByteOffset: recordsOffset + record * size + offset, as: UInt64.self)))
        }
    }
}
