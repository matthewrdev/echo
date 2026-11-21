import AppKit

package final class GlbAssetReader {
    package init() {}

    package func read(_ url: URL) throws -> GlbAsset {
        let data = try Data(contentsOf: url, options: .mappedIfSafe)
        func integer(_ offset: Int) throws -> Int {
            guard offset >= 0, offset + 4 <= data.count else { throw ModelRenderError(message: "Truncated GLB.") }
            return data.withUnsafeBytes {
                Int(UInt32(littleEndian: $0.loadUnaligned(fromByteOffset: offset, as: UInt32.self)))
            }
        }
        guard try integer(0) == 0x4654_6c67, try integer(4) == 2, try integer(8) == data.count,
            try integer(16) == 0x4e4f_534a
        else { throw ModelRenderError(message: "Expected a GLB 2 file.") }
        let jsonLength = try integer(12)
        let binary = 28 + jsonLength
        guard binary <= data.count, try integer(24 + jsonLength) == 0x004e_4942 else {
            throw ModelRenderError(message: "Missing GLB binary data.")
        }
        guard
            let root = try JSONSerialization.jsonObject(with: data.subdata(in: 20..<(20 + jsonLength)))
                as? [String: Any],
            let meshes = root["meshes"] as? [[String: Any]],
            let primitives = meshes.first?["primitives"] as? [[String: Any]],
            let primitive = primitives.first,
            let attributes = primitive["attributes"] as? [String: Int],
            let accessors = root["accessors"] as? [[String: Any]],
            let views = root["bufferViews"] as? [[String: Any]],
            let positionIndex = attributes["POSITION"], let uvIndex = attributes["TEXCOORD_0"],
            let indexIndex = primitive["indices"] as? Int
        else {
            throw ModelRenderError(message: "Unsupported GLB mesh profile.")
        }

        func accessor(_ index: Int, components: Int, componentType: Int) throws -> [[Double]] {
            guard accessors.indices.contains(index),
                let viewIndex = accessors[index]["bufferView"] as? Int, views.indices.contains(viewIndex),
                let count = accessors[index]["count"] as? Int, count >= 0,
                accessors[index]["componentType"] as? Int == componentType
            else {
                throw ModelRenderError(message: "Unsupported GLB accessor.")
            }
            let view = views[viewIndex]
            let offset = binary + (view["byteOffset"] as? Int ?? 0) + (accessors[index]["byteOffset"] as? Int ?? 0)
            let stride = view["byteStride"] as? Int ?? components * 4
            guard stride >= components * 4, offset >= 0, offset <= data.count,
                count <= (data.count - offset) / stride
            else { throw ModelRenderError(message: "GLB accessor exceeds its buffer.") }
            return try (0..<count).map { row in
                try (0..<components).map { column in
                    let bits = UInt32(try integer(offset + row * stride + column * 4))
                    return componentType == 5126 ? Double(Float(bitPattern: bits)) : Double(bits)
                }
            }
        }

        let vertices = try accessor(positionIndex, components: 3, componentType: 5126)
        let uvs = try accessor(uvIndex, components: 2, componentType: 5126)
        let indices = try accessor(indexIndex, components: 1, componentType: 5125).map { Int($0[0]) }
        guard indices.count.isMultiple(of: 3), indices.allSatisfy({ vertices.indices.contains($0) }),
            uvs.count == vertices.count,
            let materials = root["materials"] as? [[String: Any]],
            let materialIndex = primitive["material"] as? Int, materials.indices.contains(materialIndex),
            let pbr = materials[materialIndex]["pbrMetallicRoughness"] as? [String: Any],
            let baseColour = pbr["baseColorTexture"] as? [String: Any],
            let textureIndex = baseColour["index"] as? Int,
            let textures = root["textures"] as? [[String: Any]], textures.indices.contains(textureIndex),
            let sourceIndex = textures[textureIndex]["source"] as? Int,
            let images = root["images"] as? [[String: Any]], images.indices.contains(sourceIndex),
            let imageIndex = images[sourceIndex]["bufferView"] as? Int,
            views.indices.contains(imageIndex), let imageLength = views[imageIndex]["byteLength"] as? Int
        else {
            throw ModelRenderError(message: "Invalid GLB geometry or texture.")
        }
        let imageStart = binary + (views[imageIndex]["byteOffset"] as? Int ?? 0)
        guard imageStart >= 0, imageLength >= 0, imageStart <= data.count - imageLength,
            let image = NSImage(data: data.subdata(in: imageStart..<(imageStart + imageLength)))
        else {
            throw ModelRenderError(message: "Cannot decode GLB texture.")
        }
        let faces = stride(from: 0, to: indices.count, by: 3).map { Array(indices[$0..<($0 + 3)]) }
        return GlbAsset(surface: Surface(vertices: vertices, faces: faces, sourceImages: []), uvs: uvs, image: image)
    }
}
