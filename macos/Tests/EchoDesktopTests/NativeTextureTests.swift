import AppKit
import EchoRendering
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class NativeTextureTests: XCTestCase {
    func testAtlasCoordinatesSampleTheSameImageRowsInTheNativeViewer() throws {
        guard let device = MTLCreateSystemDefaultDevice() else {
            throw XCTSkip("Native rendering requires a Metal device.")
        }
        let bitmap = try XCTUnwrap(
            NSBitmapImageRep(
                bitmapDataPlanes: nil, pixelsWide: 64, pixelsHigh: 64,
                bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        for y in 0..<64 {
            for x in 0..<64 {
                bitmap.setColor(
                    NSColor(deviceRed: y < 32 ? 1 : 0, green: 0, blue: y < 32 ? 0 : 1, alpha: 1), atX: x, y: y)
            }
        }
        let image = NSImage(size: NSSize(width: 64, height: 64))
        image.addRepresentation(bitmap)
        let surface = Surface(
            vertices: [[-1, 1, 0], [1, 1, 0], [1, -1, 0], [-1, -1, 0]],
            faces: [[0, 1, 2], [0, 2, 3]], sourceImages: [])
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [],
            root: URL(fileURLWithPath: "/tmp"), surface: surface,
            uvs: [[0, 0], [1, 0], [1, 1], [0, 1]], texture: image)
        let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        camera.position = SCNVector3(0, 0, 4)
        camera.eulerAngles = SCNVector3Zero
        let lens = try XCTUnwrap(camera.camera)
        lens.usesOrthographicProjection = true
        lens.orthographicScale = 1.2
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        let rendered = renderer.snapshot(atTime: 0, with: CGSize(width: 128, height: 128), antialiasingMode: .none)
        let tiff = try XCTUnwrap(rendered.tiffRepresentation)
        let output = try XCTUnwrap(NSBitmapImageRep(data: tiff))
        let top = try XCTUnwrap(output.colorAt(x: 64, y: 32)?.usingColorSpace(.deviceRGB))
        let bottom = try XCTUnwrap(output.colorAt(x: 64, y: 96)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(top.redComponent, 0.8, "Top of mesh must sample top/red atlas row")
        XCTAssertLessThan(top.blueComponent, 0.2)
        XCTAssertLessThan(bottom.redComponent, 0.2)
        XCTAssertGreaterThan(bottom.blueComponent, 0.8, "Bottom of mesh must sample bottom/blue atlas row")
    }
}
