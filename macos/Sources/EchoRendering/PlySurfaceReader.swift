import Foundation

struct PlySurfaceReader {
    func read(_ url: URL) throws -> Surface {
        let data = try Data(contentsOf: url, options: .mappedIfSafe)
        let header = data.prefix(1024)
        guard let end = header.range(of: Data("end_header\n".utf8)),
            let text = String(data: header[..<end.lowerBound], encoding: .ascii)
        else { throw ModelRenderError(message: "Missing Echo PLY header.") }

        let lines = text.split(separator: "\n")
        guard lines.count == 8, lines[0] == "ply", lines[1] == "format binary_little_endian 1.0",
            lines[3] == "property float x", lines[4] == "property float y", lines[5] == "property float z",
            lines[7] == "property list uchar int vertex_indices",
            let vertexCount = count(lines[2], prefix: "element vertex "),
            let faceCount = count(lines[6], prefix: "element face "),
            vertexCount <= (data.count - end.upperBound) / 12
        else { throw ModelRenderError(message: "Unsupported Echo mesh PLY profile.") }
        let faceOffset = end.upperBound + vertexCount * 12
        let faceBytes = data.count - faceOffset
        guard faceBytes.isMultiple(of: 13), faceCount == faceBytes / 13 else {
            throw ModelRenderError(message: "Invalid Echo mesh PLY size.")
        }

        return try data.withUnsafeBytes { (bytes: UnsafeRawBufferPointer) in
            func integer(_ offset: Int) -> UInt32 {
                UInt32(littleEndian: bytes.loadUnaligned(fromByteOffset: offset, as: UInt32.self))
            }

            let vertices = (0..<vertexCount).map { index in
                let start = end.upperBound + index * 12
                return [0, 4, 8].map { Double(Float(bitPattern: integer(start + $0))) }
            }
            let faces = try (0..<faceCount).map { index in
                let start = faceOffset + index * 13
                let indices = [1, 5, 9].map { Int(integer(start + $0)) }
                guard bytes[start] == 3, indices.allSatisfy(vertices.indices.contains) else {
                    throw ModelRenderError(message: "Invalid Echo mesh PLY triangle.")
                }
                return indices
            }
            return Surface(vertices: vertices, faces: faces, sourceImages: [])
        }
    }

    private func count(_ line: Substring, prefix: String) -> Int? {
        guard line.hasPrefix(prefix), let value = Int(line.dropFirst(prefix.count)), value > 0 else { return nil }
        return value
    }
}
