import Foundation

struct BinaryRecordBuffer {
    let data: Data
    let size: Int
    let count: Int

    init(file: URL, size: Int) throws {
        data = try Data(contentsOf: file, options: .mappedIfSafe)
        self.size = size
        guard data.count >= 16 else { throw CLIError(message: "Truncated binary artifact") }
        let header = data.withUnsafeBytes { raw in
            (0..<4).map { Int32(littleEndian: raw.loadUnaligned(fromByteOffset: $0 * 4, as: Int32.self)) }
        }
        count = Int(header[3])
        guard header[0] == 0x3150_4345, header[1] == 1, header[2] == size,
            count >= 0, count <= (data.count - 16) / size
        else { throw CLIError(message: "Invalid binary artifact header or size") }
    }

    func integer(_ record: Int, _ offset: Int = 0) -> Int {
        data.withUnsafeBytes { raw in
            Int(Int32(littleEndian: raw.loadUnaligned(fromByteOffset: 16 + record * size + offset, as: Int32.self)))
        }
    }

    func byte(_ record: Int, _ offset: Int) -> UInt8 {
        data[16 + record * size + offset]
    }

    func float(_ record: Int, _ offset: Int = 0) -> Float {
        data.withUnsafeBytes { raw in
            Float(
                bitPattern: UInt32(
                    littleEndian: raw.loadUnaligned(fromByteOffset: 16 + record * size + offset, as: UInt32.self)))
        }
    }

    func double(_ record: Int, _ offset: Int = 0) -> Double {
        data.withUnsafeBytes { raw in
            Double(
                bitPattern: UInt64(
                    littleEndian: raw.loadUnaligned(fromByteOffset: 16 + record * size + offset, as: UInt64.self)))
        }
    }
}
