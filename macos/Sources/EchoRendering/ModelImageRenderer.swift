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
        let input = URL(fileURLWithPath: request.input)
        let surface: Surface
        let uvs: [[Double]]
        let texture: NSImage?
        if request.rawMesh == true {
            surface = try PlySurfaceReader().read(input)
            uvs = []
            texture = nil
        } else {
            let asset = try GlbAssetReader().read(input)
            surface = asset.surface
            uvs = asset.uvs
            texture = asset.image
        }
        guard !surface.faces.isEmpty,
            surface.vertices.allSatisfy({ $0.allSatisfy(\.isFinite) }),
            uvs.allSatisfy({ $0.allSatisfy(\.isFinite) })
        else { throw ModelRenderError(message: "The model must have finite geometry and at least one triangle.") }

        let scene = SCNScene()
        let background = NSColor(calibratedRed: 0.075, green: 0.085, blue: 0.10, alpha: 1)
        scene.background.contents = background
        let geometry = SurfaceGeometryBuilder().make(
            surface: surface, uvs: uvs, texture: texture, mode: request.meshMode!)
        scene.rootNode.addChildNode(SCNNode(geometry: geometry))
        let camera = SCNNode()
        camera.camera = SCNCamera()
        scene.rootNode.addChildNode(camera)
        let size = CGSize(width: request.width, height: request.height)
        let bounds = SceneBounds(positions: surface.vertices)
        let viewport: CGSize
        if let calibrated = request.camera {
            try calibrated.validate()
            viewport = calibrated.viewport(in: size)
            calibrated.configure(camera, size: viewport, bounds: bounds)
        } else {
            viewport = size
            bounds.fit(camera: camera, size: size, offset: request.view.offset, up: request.view.up)
        }
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        let image = renderer.snapshot(atTime: 0, with: viewport, antialiasingMode: .multisampling4X)
        let png = try encode(image, size: size, viewport: viewport, background: background)
        try png.write(to: URL(fileURLWithPath: request.output), options: .withoutOverwriting)
        return ModelRenderResult(
            input: request.input, output: request.output, width: request.width, height: request.height,
            view: request.view, mode: request.mode, vertices: surface.vertices.count,
            triangles: surface.faces.count, cameraImageId: request.camera?.imageId)
    }

    private func encode(_ image: NSImage, size: CGSize, viewport: CGSize, background: NSColor) throws -> Data {
        guard let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff) else {
            throw ModelRenderError(message: "Could not decode the rendered snapshot.")
        }
        let final: NSBitmapImageRep
        if size == viewport {
            final = bitmap
        } else {
            guard let snapshot = bitmap.cgImage,
                let context = CGContext(
                    data: nil, width: Int(size.width), height: Int(size.height), bitsPerComponent: 8, bytesPerRow: 0,
                    space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
            else { throw ModelRenderError(message: "Could not create the camera preview frame.") }
            context.setFillColor(background.cgColor)
            context.fill(CGRect(origin: .zero, size: size))
            context.draw(
                snapshot,
                in: CGRect(
                    x: floor((size.width - viewport.width) / 2), y: floor((size.height - viewport.height) / 2),
                    width: viewport.width, height: viewport.height))
            guard let composed = context.makeImage() else {
                throw ModelRenderError(message: "Could not compose the camera preview frame.")
            }
            final = NSBitmapImageRep(cgImage: composed)
        }
        guard let png = final.representation(using: .png, properties: [:]) else {
            throw ModelRenderError(message: "Could not encode the rendered PNG.")
        }
        return png
    }
}
