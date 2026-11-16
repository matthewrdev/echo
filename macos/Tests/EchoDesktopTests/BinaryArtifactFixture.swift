import Foundation

// Test fixtures publish the same typed little-endian buffers as the processing CLI.
enum BinaryArtifactFixture {
    static func write(_ file: URL, value: [String: Any]) throws {
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        let object =
            try JSONSerialization.jsonObject(with: JSONSerialization.data(withJSONObject: value)) as! [String: Any]
        var header: [String: Any] = [
            "storage": "echo-binary-v1", "imageId": object["imageId"] ?? "", "width": object["width"] ?? 0,
            "height": object["height"] ?? 0, "voxelSize": object["voxelSize"] ?? 0, "images": [],
        ]
        var buffers: [String: String] = [:]
        func save(_ name: String, size: Int, count: Int, bytes: Data) throws {
            let path = UUID().uuidString + ".bin"
            var data = encode([UInt32(0x3150_4345), 1, UInt32(size), UInt32(count)])
            data.append(bytes)
            try data.write(to: file.deletingLastPathComponent().appendingPathComponent(path))
            buffers[name] = path
        }
        if let depth = object["depth"] as? [NSNumber], let confidence = object["confidence"] as? [NSNumber] {
            header["kind"] = "depth"
            try save("depth", size: 4, count: depth.count, bytes: encode(depth.map { $0.floatValue.bitPattern }))
            try save(
                "confidence", size: 4, count: confidence.count,
                bytes: encode(confidence.map { $0.floatValue.bitPattern }))
        } else if let vertices = object["vertices"] as? [[NSNumber]], let faces = object["faces"] as? [[NSNumber]] {
            header["kind"] = "surface"
            let sources = object["sourceImages"] as? [String] ?? []
            let images = Array(Set(sources)).sorted()
            header["images"] = images
            try save(
                "vertices", size: 24, count: vertices.count,
                bytes: encode(vertices.flatMap { $0.map { $0.doubleValue.bitPattern } }))
            try save(
                "faces", size: 12, count: faces.count,
                bytes: encode(faces.flatMap { $0.map { UInt32(bitPattern: $0.int32Value) } }))
            try save(
                "sources", size: 4, count: sources.count,
                bytes: encode(sources.map { UInt32(images.firstIndex(of: $0)!) }))
        } else if let samples = object["samples"] as? [[String: NSNumber]] {
            header["kind"] = "volume"
            let words = samples.flatMap { sample in
                [
                    UInt32(bitPattern: sample["x"]!.int32Value), UInt32(bitPattern: sample["y"]!.int32Value),
                    UInt32(bitPattern: sample["z"]!.int32Value), sample["distance"]!.floatValue.bitPattern,
                    UInt32(bitPattern: sample["views"]!.int32Value),
                ]
            }
            try save("samples", size: 20, count: samples.count, bytes: encode(words))
        } else if let uvs = object["uvs"] as? [[NSNumber]] {
            header["kind"] = "uvs"
            try save(
                "uvs", size: 16, count: uvs.count, bytes: encode(uvs.flatMap { $0.map { $0.doubleValue.bitPattern } }))
            header["buffers"] = buffers
            let path = UUID().uuidString + ".json"
            try JSONSerialization.data(withJSONObject: header).write(
                to: file.deletingLastPathComponent().appendingPathComponent(path))
            var texture = object
            texture.removeValue(forKey: "uvs")
            texture["uvsFile"] = path
            try JSONSerialization.data(withJSONObject: texture).write(to: file, options: .atomic)
            return
        } else {
            try JSONSerialization.data(withJSONObject: object).write(to: file, options: .atomic)
            return
        }
        header["buffers"] = buffers
        try JSONSerialization.data(withJSONObject: header).write(to: file, options: .atomic)
    }

    private static func encode<T: FixedWidthInteger>(_ values: [T]) -> Data {
        var data = Data()
        for value in values {
            var encoded = value.littleEndian
            withUnsafeBytes(of: &encoded) { data.append(contentsOf: $0) }
        }
        return data
    }
}
