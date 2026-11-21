import AppKit
import Metal
import SceneKit

package final class ModelImageRenderer {
    package init() {}

    package func render(_ request: ModelRenderRequest) throws -> ModelRenderResult {
        try request.validate()
        guard let device = MTLCreateSystemDefaultDevice() else {
            throw ModelRenderError(message: "Rendering requires an available Metal GPU.")
        }
        let asset = try GlbAssetReader().read(URL(fileURLWithPath: request.input))
        guard !asset.surface.faces.isEmpty,
            asset.surface.vertices.allSatisfy({ $0.allSatisfy(\.isFinite) }),
            asset.uvs.allSatisfy({ $0.allSatisfy(\.isFinite) })
        else { throw ModelRenderError(message: "The model must have finite geometry and at least one triangle.") }

        let scene = SCNScene()
        scene.background.contents = NSColor(calibratedRed: 0.075, green: 0.085, blue: 0.10, alpha: 1)
        let geometry = SurfaceGeometryBuilder().make(
            surface: asset.surface, uvs: asset.uvs, texture: asset.image, mode: request.meshMode!)
        scene.rootNode.addChildNode(SCNNode(geometry: geometry))
        let camera = SCNNode()
        camera.camera = SCNCamera()
        scene.rootNode.addChildNode(camera)
        let size = CGSize(width: request.width, height: request.height)
        SceneBounds(positions: asset.surface.vertices).fit(
            camera: camera, size: size, offset: request.view.offset, up: request.view.up)
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        let image = renderer.snapshot(atTime: 0, with: size, antialiasingMode: .multisampling4X)
        guard let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
            let png = bitmap.representation(using: .png, properties: [:])
        else { throw ModelRenderError(message: "Could not encode the rendered PNG.") }
        try png.write(to: URL(fileURLWithPath: request.output), options: .withoutOverwriting)
        return ModelRenderResult(
            input: request.input, output: request.output, width: request.width, height: request.height,
            view: request.view, mode: request.mode, vertices: asset.surface.vertices.count,
            triangles: asset.surface.faces.count)
    }
}
