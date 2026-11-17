import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class CompletedMeshViewerTests: XCTestCase {
    @MainActor
    func testNativeViewerOpensCompletedMeshArtifact() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_PROJECT"], let runID = environment["ECHO_TEST_RUN"] else {
            throw XCTSkip("Set ECHO_TEST_PROJECT and ECHO_TEST_RUN to inspect a completed mesh.")
        }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run: RunData = try json.read(root.appendingPathComponent("runs/\(runID)/run.json"))
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let presentation: ArtifactPresentation
        do {
            presentation = try await reader.load(
                root: root, run: run, stage: .mesh, unit: nil, mode: "Solid", slice: 0.5)
        } catch {
            XCTFail("Mesh loading failed: \(String(reflecting: error))")
            return
        }
        let scene = try XCTUnwrap(presentation.scene)
        let geometry = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false)?.geometry)
        XCTAssertGreaterThan(geometry.elements.first?.primitiveCount ?? 0, 0)
        if let output = environment["ECHO_TEST_RENDER_DIR"] {
            let device = try XCTUnwrap(MTLCreateSystemDefaultDevice())
            let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
            let renderer = SCNRenderer(device: device, options: nil)
            renderer.scene = scene
            renderer.pointOfView = camera
            let rendered = renderer.snapshot(
                atTime: 0, with: CGSize(width: 1440, height: 900), antialiasingMode: .multisampling4X)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(rendered.tiffRepresentation)))
            let folder = URL(fileURLWithPath: output)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: folder.appendingPathComponent("completed-mesh-native-viewer.png"))
        }
    }

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
