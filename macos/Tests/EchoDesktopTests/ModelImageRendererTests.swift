import AppKit
import EchoRendering
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class ModelImageRendererTests: XCTestCase {
    func testRendersExactDimensionsAndSelectedMaterialWithoutChangingModel() throws {
        guard MTLCreateSystemDefaultDevice() != nil else { throw XCTSkip("Metal device required") }
        let file = try GlbAssetFixture().make(textureSource: 1)
        defer { try? FileManager.default.removeItem(at: file) }
        let original = try Data(contentsOf: file)
        for mode in ["textured", "solid", "wireframe", "normals"] {
            let output = file.deletingPathExtension().appendingPathExtension("\(mode).png")
            defer { try? FileManager.default.removeItem(at: output) }
            let request = ModelRenderRequest(
                input: file.path, output: output.path, width: 320, height: 200, view: .front, mode: mode)
            let result = try ModelImageRenderer().render(request)
            XCTAssertEqual(result.triangles, 1)
            XCTAssertEqual(result.vertices, 3)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: Data(contentsOf: output)))
            XCTAssertEqual(bitmap.pixelsWide, 320)
            XCTAssertEqual(bitmap.pixelsHigh, 200)
            // This point is inside the asymmetric test triangle, away from all edges.
            let color = try XCTUnwrap(bitmap.colorAt(x: 130, y: 125)?.usingColorSpace(.deviceRGB))
            switch mode {
            case "textured":
                XCTAssertGreaterThan(color.redComponent, 0.9)
                XCTAssertGreaterThan(color.greenComponent, 0.9)
                XCTAssertLessThan(color.blueComponent, 0.1)
            case "solid":
                XCTAssertEqual(color.redComponent, color.blueComponent, accuracy: 0.01)
                XCTAssertGreaterThan(color.redComponent, 0.4)
            case "normals":
                XCTAssertGreaterThan(color.blueComponent, color.redComponent + 0.2)
            default:
                XCTAssertLessThan(color.redComponent, 0.2)
                XCTAssertLessThan(color.greenComponent, 0.2)
            }
        }
        XCTAssertEqual(try Data(contentsOf: file), original)
    }

    func testInvalidRequestsAndExistingOutputAreRejectedBeforeRendering() throws {
        let file = try GlbAssetFixture().make(textureSource: 1)
        let output = file.deletingPathExtension().appendingPathExtension("png")
        defer {
            try? FileManager.default.removeItem(at: file)
            try? FileManager.default.removeItem(at: output)
        }
        for width in [0, -1, 8193, Int.max] {
            let request = ModelRenderRequest(
                input: file.path, output: output.path, width: width, height: 200, view: .front, mode: "textured")
            XCTAssertThrowsError(try ModelImageRenderer().render(request))
        }
        let huge = ModelRenderRequest(
            input: file.path, output: output.path, width: 8192, height: 8192, view: .front, mode: "textured")
        XCTAssertThrowsError(try huge.validate())
        let original = Data("keep existing image".utf8)
        try original.write(to: output)
        let request = ModelRenderRequest(
            input: file.path, output: output.path, width: 320, height: 200, view: .front, mode: "textured")
        XCTAssertThrowsError(try ModelImageRenderer().render(request))
        XCTAssertEqual(try Data(contentsOf: output), original)
    }

    func testAllCameraPresetsFitTheWholeBoundsInPortraitAndLandscape() throws {
        guard let device = MTLCreateSystemDefaultDevice() else { throw XCTSkip("Metal device required") }
        let bounds = SceneBounds(positions: [[-10, -2, -6], [2, 5, 8]])
        for view in ModelRenderView.allCases {
            for size in [CGSize(width: 320, height: 200), CGSize(width: 160, height: 300)] {
                let scene = SCNScene()
                let camera = SCNNode()
                camera.camera = SCNCamera()
                scene.rootNode.addChildNode(camera)
                bounds.fit(camera: camera, size: size, offset: view.offset, up: view.up)
                let renderer = SCNRenderer(device: device, options: nil)
                renderer.scene = scene
                renderer.pointOfView = camera
                _ = renderer.snapshot(atTime: 0, with: size, antialiasingMode: .none)
                for x in [bounds.minimum.x, bounds.maximum.x] {
                    for y in [bounds.minimum.y, bounds.maximum.y] {
                        for z in [bounds.minimum.z, bounds.maximum.z] {
                            let projected = renderer.projectPoint(SCNVector3(x, y, z))
                            XCTAssertTrue((0...size.width).contains(projected.x), "\(view): x outside image")
                            XCTAssertTrue((0...size.height).contains(projected.y), "\(view): y outside image")
                            XCTAssertTrue(
                                (0...1).contains(projected.z),
                                "\(view): depth \(projected.z), camera \(camera.position), local \(camera.convertPosition(SCNVector3(x, y, z), from: nil))"
                            )
                        }
                    }
                }
            }
        }
    }
}
