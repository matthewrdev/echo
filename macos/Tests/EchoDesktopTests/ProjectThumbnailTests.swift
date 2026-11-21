import AppKit
import EchoRendering
import Metal
import XCTest

@testable import EchoDesktop

final class ProjectThumbnailTests: XCTestCase {
    func testFindsEarlierMeshAndInvalidatesRemovedArtifact() async throws {
        guard MTLCreateSystemDefaultDevice() != nil else { throw XCTSkip("Metal device required") }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try write(
            root: root, path: "runs/old/run.json",
            value: [
                "created": "2026-11-10T00:00:00Z", "artifacts": ["mesh": "artifacts/mesh/old"],
            ])
        try write(
            root: root, path: "runs/new/run.json",
            value: [
                "created": "2026-11-11T00:00:00Z", "artifacts": ["mesh": "artifacts/mesh/missing"],
            ])
        try write(
            root: root, path: "artifacts/mesh/old/mesh.json",
            value: [
                "vertices": [[-1, -1, 0], [1, -1, 0], [0, 1, 0]], "faces": [[0, 1, 2]], "sourceImages": [],
            ])
        let loader = ProjectThumbnailLoader(
            scenes: NativeSceneFactory(), json: ArtifactJSONReader(), glb: GlbAssetReader())
        let first = await loader.load(root: root)
        let image = try XCTUnwrap(first)
        XCTAssertEqual(image.size, NSSize(width: 240, height: 180))
        let cached = await loader.load(root: root)
        XCTAssertTrue(cached === image)
        try FileManager.default.removeItem(at: root.appendingPathComponent("artifacts/mesh/old/mesh.json"))
        let removed = await loader.load(root: root)
        XCTAssertNil(removed)
    }

    func testTexturedMeshUsesItsSavedMeshFile() async throws {
        guard MTLCreateSystemDefaultDevice() != nil else { throw XCTSkip("Metal device required") }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try write(
            root: root, path: "runs/texture/run.json",
            value: [
                "created": "2026-11-10T00:00:00Z", "artifacts": ["texture": "artifacts/texture/saved"],
            ])
        try write(
            root: root, path: "artifacts/texture/saved/texture.json",
            value: [
                "meshFile": "mesh.json", "size": 2, "uvs": [[0, 0], [1, 0], [0.5, 1]],
            ])
        try write(
            root: root, path: "artifacts/texture/saved/mesh.json",
            value: [
                "vertices": [[-1, -1, 0], [1, -1, 0], [0, 1, 0]], "faces": [[0, 1, 2]], "sourceImages": [],
            ])
        let bitmap = try XCTUnwrap(
            NSBitmapImageRep(
                bitmapDataPlanes: nil, pixelsWide: 2, pixelsHigh: 2, bitsPerSample: 8, samplesPerPixel: 3,
                hasAlpha: false, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        for x in 0..<2 {
            for y in 0..<2 { bitmap.setColor(NSColor(deviceRed: 1, green: 0, blue: 0, alpha: 1), atX: x, y: y) }
        }
        try XCTUnwrap(bitmap.representation(using: .jpeg, properties: [:])).write(
            to: root.appendingPathComponent("artifacts/texture/saved/atlas.jpg"))
        let loader = ProjectThumbnailLoader(
            scenes: NativeSceneFactory(), json: ArtifactJSONReader(), glb: GlbAssetReader())
        let loaded = await loader.load(root: root)
        let image = try XCTUnwrap(loaded)
        let rendered = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
        let centre = try XCTUnwrap(rendered.colorAt(x: 120, y: 90)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(centre.redComponent, 0.8)
        XCTAssertLessThan(centre.greenComponent, 0.2)
    }

    func testSavedProjectThumbnail() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_THUMBNAIL_PROJECT"],
            let output = environment["ECHO_TEST_THUMBNAIL_OUTPUT"]
        else { throw XCTSkip("Set thumbnail project and output to inspect a saved mesh") }
        let loader = ProjectThumbnailLoader(
            scenes: NativeSceneFactory(), json: ArtifactJSONReader(), glb: GlbAssetReader())
        let loaded = await loader.load(root: URL(fileURLWithPath: project))
        let image = try XCTUnwrap(loaded)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: URL(fileURLWithPath: output))
    }

    private func write(root: URL, path: String, value: [String: Any]) throws {
        let file = root.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try BinaryArtifactFixture.write(file, value: value)
    }
}
