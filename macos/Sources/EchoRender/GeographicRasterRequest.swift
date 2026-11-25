import EchoRendering
import Foundation

struct GeographicRasterRequest: Decodable {
    let source: GeographicModelSource
    let directory: String
    let size: Int?

    func render() throws -> Bool {
        let imageSize = size ?? 1536
        guard (1...4096).contains(imageSize) else {
            throw ModelRenderError(message: "The geographic raster size must be between 1 and 4096 pixels.")
        }
        let model = try GeographicModelReader().read(source)
        let overlay = try ModelMapProjector().render(
            surface: model.surface, uvs: model.uvs, texture: model.texture, origin: source.origin, size: imageSize)
        try GeographicRasterWriter().write(overlay, directory: URL(fileURLWithPath: directory, isDirectory: true))
        return true
    }
}
