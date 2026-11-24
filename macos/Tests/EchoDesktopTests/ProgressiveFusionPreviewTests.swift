import AppKit
import EchoRendering
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class ProgressiveFusionPreviewTests: XCTestCase {
    func testSavedPreviewGrowsWithStableViewerIdentity() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let reader = SavedFusionArtifactReader(json: ArtifactJSONReader(), scenes: NativeSceneFactory())
        for count in [20, 40] {
            let folder = root.appendingPathComponent(String(count))
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try PackedFixture.fusion(folder.appendingPathComponent("preview.bin"), count: count, voxel: 0.5)
            let presentation = try await reader.load(
                folder: folder, mode: "Surface", slice: 0.5,
                metricScale: true, sceneIdentity: "run-fusion", running: true)
            XCTAssertEqual(presentation.sceneIdentity, "run-fusion")
            XCTAssertTrue(presentation.detail.contains("LIVE PREVIEW"))
            let scene = try XCTUnwrap(presentation.scene)
            let geometry = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false)?.geometry)
            XCTAssertEqual(geometry.sources(for: .vertex).first?.vectorCount, count)
            if let output = ProcessInfo.processInfo.environment["ECHO_TEST_RENDER_DIR"],
                let device = MTLCreateSystemDefaultDevice()
            {
                let renderer = SCNRenderer(device: device, options: nil)
                renderer.scene = scene
                renderer.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
                let image = renderer.snapshot(
                    atTime: 0, with: CGSize(width: 600, height: 400), antialiasingMode: .multisampling4X)
                let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
                try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
                try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                    to: URL(fileURLWithPath: output).appendingPathComponent("fusion-progress-\(count).png"))
            }
        }
    }
}
