import AppKit
import EchoRendering
import Metal
import XCTest

@testable import EchoDesktop

final class BinaryMeshViewerTests: XCTestCase {
    @MainActor
    func testBinaryMeshLoadsInMeshTextureAndGeographicViews() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let surface: [String: Any] = [
            "vertices": [[0, 0, 0], [1, 0, 0], [0, 0, 1]], "faces": [[0, 1, 2]], "sourceImages": [""],
        ]
        try BinaryArtifactFixture.write(root.appendingPathComponent("mesh/mesh.json"), value: surface)
        try BinaryArtifactFixture.write(
            root.appendingPathComponent("alignment/scene.json"),
            value: [
                "frame": "local metres", "origin": ["latitude": -35, "longitude": 150],
                "cameras": [
                    [
                        "imageId": "photo", "centre": [0, 0, -2], "rotation": [1, 0, 0, 0, 1, 0, 0, 0, 1],
                        "focalPixels": 50, "source": "aligned",
                    ]
                ],
            ])
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let snapshot = try JSONDecoder().decode(
            ProjectData.self,
            from: Data(
                #"{"id":"test","name":"Test","revision":1,"images":[{"id":"photo","name":"photo.jpg","source":"photo.jpg","preview":"preview.jpg","width":100,"height":80,"camera":"test","focalPixels":50,"enabled":true,"sets":[],"metadata":{}}],"settings":{},"hints":[]}"#
                    .utf8))
        for textured in [false, true] {
            var artifacts = ["mesh": "mesh", "alignment": "alignment"]
            if textured {
                artifacts["texture"] = "texture"
                try BinaryArtifactFixture.write(root.appendingPathComponent("texture/atlas-mesh.json"), value: surface)
                try BinaryArtifactFixture.write(
                    root.appendingPathComponent("texture/texture.json"),
                    value: ["uvs": [[0, 0], [1, 0], [0, 1]], "size": 8, "meshFile": "atlas-mesh.json"])
                let bitmap = try XCTUnwrap(
                    NSBitmapImageRep(
                        bitmapDataPlanes: nil, pixelsWide: 8, pixelsHigh: 8, bitsPerSample: 8,
                        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                        bytesPerRow: 0, bitsPerPixel: 0))
                try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                    to: root.appendingPathComponent("texture/atlas.jpg"))
            }
            let run = RunData(
                id: "saved", status: "completed", target: "mesh", snapshot: snapshot,
                artifacts: artifacts, error: nil, progress: nil)
            for mode in MeshViewMode.allCases {
                let presentation = try await reader.load(
                    root: root, run: run, stage: .mesh, unit: nil, mode: mode.rawValue, slice: 0.5)
                let geometry = try XCTUnwrap(
                    presentation.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.geometry)
                XCTAssertEqual(geometry.elements.first?.primitiveCount, 1)
                XCTAssertEqual(presentation.cameraInspection?.cameraOrder, ["photo"])
                XCTAssertNotNil(presentation.scene?.rootNode.childNode(withName: "photo", recursively: false))
            }
            if textured {
                let presentation = try await reader.load(
                    root: root, run: run, stage: .texture, unit: nil, mode: "Textured model", slice: 0.5)
                XCTAssertNotNil(presentation.scene)
                XCTAssertEqual(presentation.cameraInspection?.cameraOrder, ["photo"])
                XCTAssertNotNil(presentation.scene?.rootNode.childNode(withName: "photo", recursively: false))
            }
            if MTLCreateSystemDefaultDevice() != nil {
                let geographic = try GeoreferenceArtifactReader().load(root: root, run: run)
                XCTAssertNotNil(geographic.overlay)
                XCTAssertNotNil(geographic.presentation.scene)
                XCTAssertEqual(geographic.presentation.cameraInspection?.cameraOrder, ["photo"])
            }
        }
    }
}
