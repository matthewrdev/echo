import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class DenseCloudPreviewTests: XCTestCase {
    func testSamplingSpansAllImagesWithinBudgetAndPreservesPackedColors() throws {
        let file = try fixture(count: 101)
        defer { try? FileManager.default.removeItem(at: file) }
        let preview = try DenseCloudPreviewReader().read(file: file, expectedCount: 101, limit: 10, minimumSupport: 0)
        XCTAssertEqual(preview.count, 10)
        let source = try XCTUnwrap(preview.geometry.sources(for: .vertex).first)
        let first = source.data.withUnsafeBytes { $0.loadUnaligned(as: Float.self) }
        let last = source.data.withUnsafeBytes { $0.loadUnaligned(fromByteOffset: 9 * 16, as: Float.self) }
        XCTAssertEqual(first, 5)
        XCTAssertEqual(last, 95)
        XCTAssertEqual(source.data.count, 160)
        let colors = try XCTUnwrap(preview.geometry.sources(for: .color).first)
        XCTAssertEqual(colors.data[12], 220)
        XCTAssertEqual(colors.data[15], 255)
        let filtered = try DenseCloudPreviewReader().read(
            file: file, expectedCount: 101, limit: 101, minimumSupport: 0.6)
        XCTAssertEqual(filtered.count, 51)
        XCTAssertThrowsError(
            try DenseCloudPreviewReader().read(file: file, expectedCount: 102, limit: 10, minimumSupport: 0))
        var truncated = try Data(contentsOf: file)
        truncated.removeLast()
        try truncated.write(to: file)
        XCTAssertThrowsError(
            try DenseCloudPreviewReader().read(file: file, expectedCount: 101, limit: 10, minimumSupport: 0))
    }

    func testPercentageThresholdIncludesPointsAtTheFloatPrecisionBoundary() throws {
        for threshold in [0.65, 0.7, 0.8] {
            let file = try fixture(count: 3, confidence: Float(threshold))
            defer { try? FileManager.default.removeItem(at: file) }
            let reader = DenseCloudPreviewReader()
            XCTAssertEqual(
                try reader.read(file: file, expectedCount: 3, limit: 3, minimumSupport: threshold).count, 3)
            XCTAssertEqual(
                try reader.read(file: file, expectedCount: 3, limit: 3, minimumSupport: threshold + 0.01).count, 0)
        }
    }

    func testPackedPointGeometryActuallyRendersWithUnlitColor() throws {
        guard let device = MTLCreateSystemDefaultDevice() else { throw XCTSkip("Metal unavailable") }
        let file = try fixture(count: 3)
        defer { try? FileManager.default.removeItem(at: file) }
        let preview = try DenseCloudPreviewReader().read(file: file, expectedCount: 3, limit: 3, minimumSupport: 0)
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: file.deletingLastPathComponent(),
            cloudGeometry: preview.geometry)
        scene.background.contents = NSColor.white
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
        let snapshot = renderer.snapshot(
            atTime: 0, with: CGSize(width: 256, height: 256), antialiasingMode: .multisampling4X)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(snapshot.tiffRepresentation)))
        var redPixels = 0
        for y in 0..<256 {
            for x in 0..<256 {
                if let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB),
                    color.redComponent > color.greenComponent + 0.1
                {
                    redPixels += 1
                }
            }
        }
        XCTAssertGreaterThan(redPixels, 0, "The implicit point indices and packed vertex colors must render")
    }

    func testSavedDenseCloudAtMaximumViewportDetail() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_CLOUD_PROJECT"], let id = environment["ECHO_TEST_CLOUD_RUN"] else {
            throw XCTSkip("Saved dense cloud not configured")
        }
        let root = URL(fileURLWithPath: path)
        let run = try JSONDecoder().decode(
            RunData.self, from: Data(contentsOf: root.appendingPathComponent("runs/\(id)/run.json")))
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let presentation = try await reader.load(
            root: root, run: run, stage: .cloud, unit: nil, mode: "All support", slice: 0.5,
            cloudDetail: .percentage(100))
        let scene = try XCTUnwrap(presentation.scene)
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        XCTAssertEqual(artifact.geometry?.sources(for: .vertex).first?.vectorCount, 4_000_000)
        XCTAssertTrue(presentation.detail.contains("4,000,000 sampled points"))
        guard let output = environment["ECHO_TEST_RENDER_DIR"], let device = MTLCreateSystemDefaultDevice() else {
            return
        }
        let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        let box = artifact.boundingBox
        let bounds = SceneBounds(positions: [[box.min.x, box.min.y, box.min.z], [box.max.x, box.max.y, box.max.z]])
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        scene.background.contents = NSColor.white
        for node in scene.rootNode.childNodes where node !== artifact && node !== camera { node.isHidden = true }
        for direction in [SceneViewDirection.north, .east, .top] {
            bounds.fit(camera: camera, size: CGSize(width: 1100, height: 700), direction: direction)
            let snapshot = renderer.snapshot(
                atTime: 0, with: CGSize(width: 1100, height: 700), antialiasingMode: .multisampling4X)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(snapshot.tiffRepresentation)))
            assertCloudRendered(bitmap)
            try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: URL(fileURLWithPath: output).appendingPathComponent("dense-\(direction).png"))
        }
        let highlighter = CloudCameraHighlight()
        let frustum = try XCTUnwrap(presentation.cloudInspection?.frustums.values.first)
        highlighter.show(frustum, on: artifact)
        bounds.fit(camera: camera, size: CGSize(width: 2200, height: 1400), direction: .north)
        let snapshot = renderer.snapshot(
            atTime: 0, with: CGSize(width: 2200, height: 1400), antialiasingMode: .multisampling4X)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(snapshot.tiffRepresentation)))
        assertCloudRendered(bitmap)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
            to: URL(fileURLWithPath: output).appendingPathComponent("dense-highlight.png"))
        highlighter.clear()
    }

    private func assertCloudRendered(_ bitmap: NSBitmapImageRep) {
        var cloudPixels = 0
        var backgroundPixels = 0
        for y in stride(from: 0, to: bitmap.pixelsHigh, by: 8) {
            for x in stride(from: 0, to: bitmap.pixelsWide, by: 8) {
                guard let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB) else { continue }
                let brightness = (color.redComponent + color.greenComponent + color.blueComponent) / 3
                if brightness > 0.1 && brightness < 0.98 { cloudPixels += 1 }
                if brightness >= 0.98 { backgroundPixels += 1 }
            }
        }
        XCTAssertGreaterThan(cloudPixels, 100, "The saved cloud must draw, including both camera highlight passes")
        XCTAssertGreaterThan(backgroundPixels, 100, "The renderer must not return an aborted black frame")
    }

    private func fixture(count: Int, confidence: Float? = nil) throws -> URL {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(
            "echo-preview-\(UUID().uuidString).ply")
        var data = Data(
            "ply\nformat binary_little_endian 1.0\nelement vertex \(count)\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nproperty int view\nproperty float confidence\nend_header\n"
                .utf8)
        for index in 0..<count {
            for value in [Float(index), 0, 0] {
                var bits = value.bitPattern.littleEndian
                withUnsafeBytes(of: &bits) { data.append(contentsOf: $0) }
            }
            data.append(contentsOf: [220, 40, 10])
            var view = Int32(index).littleEndian
            withUnsafeBytes(of: &view) { data.append(contentsOf: $0) }
            var support = (confidence ?? Float(index.isMultiple(of: 2) ? 1 : 0.3)).bitPattern.littleEndian
            withUnsafeBytes(of: &support) { data.append(contentsOf: $0) }
        }
        try data.write(to: file)
        return file
    }
}
