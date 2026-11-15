import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class MeshViewTests: XCTestCase {
    func testNormalsPreserveFaceWindingAndHandleDegenerateTriangles() throws {
        let surface = Surface(
            vertices: [[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]],
            faces: [[0, 1, 2], [0, 2, 1], [0, 2, 3], [0, 3, 1], [0, 0, 0]], sourceImages: [])
        let geometry = SurfaceGeometryBuilder().make(surface: surface, uvs: [], texture: nil, mode: .normals)
        let source = try XCTUnwrap(geometry.sources(for: .color).first)
        let colors = source.data.withUnsafeBytes { Array($0.bindMemory(to: SIMD4<Float>.self)) }
        let expected: [SIMD4<Float>] = [
            SIMD4(0.5, 0.5, 1, 1), SIMD4(0.5, 0.5, 0, 1), SIMD4(1, 0.5, 0.5, 1),
            SIMD4(0.5, 1, 0.5, 1), SIMD4(0.5, 0.5, 0.5, 1),
        ]
        XCTAssertEqual(geometry.elements.first?.primitiveCount, surface.faces.count)
        XCTAssertEqual(colors.count, surface.faces.count * 3)
        for (index, color) in colors.enumerated() {
            XCTAssertEqual(color, expected[index / 3], "Shared vertices must not average opposing face normals")
        }
    }

    func testNativeRenderDistinguishesNormalsAndWireframeFromTexturedFill() throws {
        guard let device = MTLCreateSystemDefaultDevice() else {
            throw XCTSkip("Native rendering requires a Metal device.")
        }
        let surface = Surface(
            vertices: [[-1, -1, 0], [1, -1, 0], [0, 1, 0]], faces: [[0, 1, 2]], sourceImages: [])
        let texture = NSImage(size: NSSize(width: 8, height: 8), flipped: false) { rect in
            NSColor.red.setFill()
            rect.fill()
            return true
        }
        for mode in MeshViewMode.allCases {
            let scene = NativeSceneFactory().make(
                reconstruction: nil, points: [], photos: [], root: URL(fileURLWithPath: "/tmp"),
                surface: surface, uvs: [[0, 0], [1, 0], [0.5, 1]], texture: texture, meshMode: mode)
            scene.background.contents = NSColor.black
            scene.rootNode.childNode(withName: "ground-grid", recursively: false)?.removeFromParentNode()
            let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
            camera.position = SCNVector3(0, 0, 4)
            camera.eulerAngles = SCNVector3Zero
            camera.camera?.usesOrthographicProjection = true
            camera.camera?.orthographicScale = 1.2
            let renderer = SCNRenderer(device: device, options: nil)
            renderer.scene = scene
            renderer.pointOfView = camera
            let image = renderer.snapshot(atTime: 0, with: CGSize(width: 128, height: 128), antialiasingMode: .none)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
            let center = try XCTUnwrap(bitmap.colorAt(x: 64, y: 64)?.usingColorSpace(.deviceRGB))
            switch mode {
            case .textured:
                XCTAssertGreaterThan(center.redComponent, 0.8)
                XCTAssertLessThan(center.blueComponent, 0.2)
            case .solid:
                XCTAssertEqual(center.redComponent, center.blueComponent, accuracy: 0.01)
                XCTAssertGreaterThan(center.redComponent, 0.4)
            case .wireframe:
                XCTAssertLessThan(center.redComponent + center.greenComponent + center.blueComponent, 0.05)
                let visiblePixels = (0..<128).flatMap { y in
                    (0..<128).compactMap { x in bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB) }
                }.filter { $0.greenComponent > 0.2 }
                XCTAssertGreaterThan(visiblePixels.count, 100, "Triangle edges must remain visible")
            case .normals:
                XCTAssertGreaterThan(center.blueComponent, 0.9)
                XCTAssertGreaterThan(center.blueComponent, center.redComponent + 0.1)
                XCTAssertEqual(center.redComponent, center.greenComponent, accuracy: 0.01)
            }
        }
    }
}
