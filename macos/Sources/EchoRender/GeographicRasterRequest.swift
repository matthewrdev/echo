import EchoRendering
import Foundation

struct GeographicRasterRequest: Decodable {
    let source: GeographicModelSource
    let directory: String

    func render() throws -> Bool {
        let model = try GeographicModelReader().read(source)
        let overlay = try ModelMapProjector().render(
            surface: model.surface, uvs: model.uvs, texture: model.texture, origin: source.origin)
        try GeographicRasterWriter().write(overlay, directory: URL(fileURLWithPath: directory, isDirectory: true))
        return true
    }
}
