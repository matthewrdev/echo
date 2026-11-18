import Foundation

struct FeatureDescriptorSlice {
    let data: Data
    let offset: Int
    let count: Int

    var values: [Double] {
        data.withUnsafeBytes { bytes in
            (0..<count).map {
                Double(
                    Float(
                        bitPattern: UInt32(
                            littleEndian: bytes.loadUnaligned(fromByteOffset: offset + $0 * 4, as: UInt32.self))))
            }
        }
    }
}
