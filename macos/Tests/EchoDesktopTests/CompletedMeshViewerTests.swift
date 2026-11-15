import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class CompletedMeshViewerTests: XCTestCase {
    func testNativeViewerOpensAndRendersCompletedGlb() throws {
        guard let path = ProcessInfo.processInfo.environment["ECHO_TEST_COMPLETED_GLB"] else {
            throw XCTSkip("Set ECHO_TEST_COMPLETED_GLB to inspect a completed reconstruction.")
        }
        guard let device = MTLCreateSystemDefaultDevice() else {
            throw XCTSkip("Native rendering requires a Metal device.")
        }
        let file = URL(fileURLWithPath: path)
        let asset = try GlbAssetReader().read(file)
        XCTAssertFalse(asset.surface.faces.isEmpty)
        XCTAssertEqual(asset.surface.vertices.count, asset.uvs.count)
        XCTAssertTrue(asset.surface.vertices.allSatisfy { $0.allSatisfy(\.isFinite) })
        XCTAssertTrue(asset.uvs.allSatisfy { $0.allSatisfy { $0.isFinite && $0 >= 0 && $0 <= 1 } })
        for mode in MeshViewMode.allCases {
            try render(asset: asset, file: file, mode: mode, device: device)
        }
    }

    private func render(asset: GlbAsset, file: URL, mode: MeshViewMode, device: MTLDevice) throws {
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: file.deletingLastPathComponent(),
            surface: asset.surface, uvs: asset.uvs, texture: asset.image, meshMode: mode)
        let geometry = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false)?.geometry)
        XCTAssertEqual(geometry.elements.first?.primitiveCount, asset.surface.faces.count)
        let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        let bounds = SceneBounds(positions: asset.surface.vertices)
        let size = CGSize(width: 1440, height: 900)
        bounds.fit(camera: camera, size: size)
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        renderer.autoenablesDefaultLighting = false
        let rendered = renderer.snapshot(atTime: 0, with: size, antialiasingMode: .multisampling4X)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(rendered.tiffRepresentation)))
        XCTAssertEqual(bitmap.pixelsWide, 1440)
        XCTAssertEqual(bitmap.pixelsHigh, 900)
        if let output = ProcessInfo.processInfo.environment["ECHO_TEST_RENDER_DIR"] {
            let folder = URL(fileURLWithPath: output)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: folder.appendingPathComponent("completed-glb-\(mode.rawValue.lowercased())-native-viewer.png"))
        }
    }
}
