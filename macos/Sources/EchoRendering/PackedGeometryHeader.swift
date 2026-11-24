import Foundation

struct PackedGeometryHeader: Decodable {
    let storage: String
    let kind: String
    let buffers: [String: String]

    func buffer(_ name: String, size: Int, beside file: URL) throws -> BinaryRecordBuffer {
        guard let name = buffers[name], !name.isEmpty,
            URL(fileURLWithPath: name).lastPathComponent == name, name != ".", name != ".."
        else { throw ModelRenderError(message: "Invalid binary artifact buffer path.") }
        return try BinaryRecordBuffer(file: file.deletingLastPathComponent().appendingPathComponent(name), size: size)
    }
}
