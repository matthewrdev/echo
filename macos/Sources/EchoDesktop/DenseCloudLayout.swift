import Foundation

struct DenseCloudLayout {
    let offset: Int
    static let stride = 23

    init(prefix: Data, expectedCount: Int, fileSize: Int) throws {
        guard let end = prefix.range(of: Data("end_header\n".utf8)) else {
            throw CLIError(message: "Dense cloud has no supported PLY header.")
        }
        offset = end.upperBound
        let header = String(decoding: prefix[..<offset], as: UTF8.self)
        let expected =
            "ply\nformat binary_little_endian 1.0\nelement vertex \(expectedCount)\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nproperty int view\nproperty float confidence\nend_header\n"
        guard expectedCount >= 0, expectedCount <= Int(UInt32.max), header == expected,
            fileSize >= offset, (fileSize - offset) / Self.stride == expectedCount,
            (fileSize - offset) % Self.stride == 0
        else { throw CLIError(message: "Dense cloud layout or point count does not match its checkpoint.") }
    }
}
