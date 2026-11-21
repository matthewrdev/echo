import AppKit
import EchoRendering
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class CompletedCameraViewTests: XCTestCase {
    @MainActor
    func testSavedArtifactFromSourceCameras() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_PROJECT"],
            let runID = environment["ECHO_TEST_RUN"],
            let output = environment["ECHO_TEST_RENDER_DIR"]
        else {
            throw XCTSkip("Set a saved project, run and render directory to inspect source camera views.")
        }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run: RunData = try json.read(root.appendingPathComponent("runs/\(runID)/run.json"))
        let stage = PipelineStage(rawValue: environment["ECHO_TEST_STAGE"] ?? "export") ?? .export
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let presentation = try await reader.load(
            root: root, run: run, stage: stage, unit: nil,
            mode: environment["ECHO_TEST_VIEW_MODE"] ?? "Textured", slice: 0.5)
        var scene = try XCTUnwrap(presentation.scene)
        let inspection = try XCTUnwrap(presentation.cameraInspection)
        if let reference = environment["ECHO_TEST_REFERENCE_GLB"] {
            let asset = try GlbAssetReader().read(URL(fileURLWithPath: reference))
            scene = NativeSceneFactory().make(
                reconstruction: nil, points: [], photos: [], root: root,
                surface: asset.surface, uvs: asset.uvs, texture: asset.image)
        }
        if let mesh = environment["ECHO_TEST_INSPECT_MESH"] {
            let surface: Surface = try json.read(URL(fileURLWithPath: mesh))
            scene = NativeSceneFactory().make(
                reconstruction: nil, points: [], photos: [], root: root,
                surface: surface, meshMode: .solid)
        }
        if let texturePath = environment["ECHO_TEST_TEXTURE_IMAGE"] {
            let texture = try XCTUnwrap(NSImage(contentsOfFile: texturePath))
            let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
            try XCTUnwrap(artifact.geometry?.firstMaterial).diffuse.contents = texture
        }
        let requested = environment["ECHO_TEST_CAMERA_IDS"]?.split(separator: ",").map(String.init)
        let cameraIDs =
            try requested ?? [XCTUnwrap(inspection.cameraOrder.dropFirst(inspection.cameraOrder.count / 2).first)]
        let folder = URL(fileURLWithPath: output)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        for imageID in cameraIDs {
            let photo = try XCTUnwrap(inspection.photos[imageID])
            let frustum = try XCTUnwrap(inspection.frustums[imageID])
            let width = CGFloat(Int(environment["ECHO_TEST_RENDER_WIDTH"] ?? "1600") ?? 1600)
            let size = CGSize(width: width, height: (width * CGFloat(photo.height) / CGFloat(photo.width)).rounded())
            let view = SourceCameraSceneView(frame: NSRect(origin: .zero, size: size))
            view.antialiasingMode = .multisampling4X
            view.show(scene: scene, frustum: frustum)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(view.snapshot().tiffRepresentation)))
            XCTAssertGreaterThan(bitmap.pixelsWide, 0)
            XCTAssertGreaterThan(bitmap.pixelsHigh, 0)
            let file = folder.appendingPathComponent("camera-\(imageID).png")
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: file)
            let provenance: [String: Any] = [
                "project": project, "runId": runID, "stage": stage.rawValue,
                "artifact": run.artifacts[stage.rawValue] ?? "", "imageId": imageID,
                "sourcePhoto": photo.name, "sourcePath": root.appendingPathComponent(photo.source).path,
                "cameraPosition": [frustum.centre.x, frustum.centre.y, frustum.centre.z],
                "verticalFieldOfViewDegrees": 2 * atan(frustum.halfHeight) * 180 / .pi,
                "referenceGLB": environment["ECHO_TEST_REFERENCE_GLB"] ?? "",
                "inspectionMesh": environment["ECHO_TEST_INSPECT_MESH"] ?? "",
                "textureOverride": environment["ECHO_TEST_TEXTURE_IMAGE"] ?? "",
                "view": "Echo native source-camera viewport; original pose and lens",
            ]
            try JSONSerialization.data(withJSONObject: provenance, options: [.prettyPrinted, .sortedKeys])
                .write(to: folder.appendingPathComponent("camera-\(imageID).json"))
        }
    }
}
