import AppKit

package struct GeographicModelReader {
    package init() {}

    package func read(_ source: GeographicModelSource) throws -> GeographicModel {
        let file = URL(fileURLWithPath: source.path)
        if file.pathExtension.lowercased() == "glb" {
            let asset = try GlbAssetReader().read(file)
            return GeographicModel(surface: asset.surface, uvs: asset.uvs, texture: asset.image)
        }

        let decoder = JSONDecoder()
        let data = try Data(contentsOf: file, options: .mappedIfSafe)
        let geometry = PackedGeometryReader()
        let surface: Surface
        if let header = try? decoder.decode(PackedGeometryHeader.self, from: data) {
            try validate(header, kind: "surface")
            surface = try geometry.surface(
                vertices: header.buffer("vertices", size: 24, beside: file),
                faces: header.buffer("faces", size: 12, beside: file), images: [])
        } else {
            surface = try decoder.decode(Surface.self, from: data)
        }

        var uv: [[Double]] = []
        if let path = source.coordinates {
            let file = URL(fileURLWithPath: path)
            let data = try Data(contentsOf: file, options: .mappedIfSafe)
            if let header = try? decoder.decode(PackedGeometryHeader.self, from: data) {
                try validate(header, kind: "uvs")
                uv = try geometry.coordinates(header.buffer("uvs", size: 16, beside: file))
            } else if let coordinates = try? decoder.decode([[Double]].self, from: data) {
                uv = coordinates
            } else {
                uv = try decoder.decode(InlineTextureCoordinates.self, from: data).uvs ?? []
            }
        }
        let texture = source.atlas.flatMap { NSImage(contentsOfFile: $0) }
        return GeographicModel(surface: surface, uvs: uv, texture: texture)
    }

    private func validate(_ header: PackedGeometryHeader, kind: String) throws {
        guard header.storage == "echo-binary-v1", header.kind == kind else {
            throw ModelRenderError(message: "Unsupported binary geometry artifact.")
        }
    }
}
