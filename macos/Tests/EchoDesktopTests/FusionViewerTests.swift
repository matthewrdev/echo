import AppKit
import EchoRendering
import Metal
import SceneKit
import XCTest
import simd

@testable import EchoDesktop

final class FusionViewerTests: XCTestCase {
    func testSurfaceReconstructsKnownPlaneAndPreservesSupport() throws {
        let volume = plane()
        let surface = try FusionSurfaceBuilder().build(volume)
        XCTAssertEqual(surface.triangleCount, 18)
        XCTAssertEqual(surface.positions.count, 16)
        XCTAssertTrue(surface.positions.allSatisfy { abs($0.z - 0.125) < 0.000001 })
        XCTAssertTrue(surface.normals.allSatisfy { abs($0.z - 1) < 0.000001 })
        XCTAssertEqual(Set(surface.support), [2, 12])
        XCTAssertTrue(surface.indices.allSatisfy { Int($0) < surface.positions.count })
        let repeated = try FusionSurfaceBuilder().build(volume)
        XCTAssertEqual(surface.positions, repeated.positions)
        XCTAssertEqual(surface.indices, repeated.indices)
    }

    func testUnknownSamplesDoNotBecomeInventedSurface() throws {
        let complete = plane()
        let missing = FusedVolume(
            voxelSize: complete.voxelSize,
            samples: complete.samples.filter { !($0.x == 2 && $0.y == 2) })
        let surface = try FusionSurfaceBuilder().build(missing)
        XCTAssertLessThan(surface.triangleCount, 18)
        // All cells adjoining the missing column remain unknown.
        XCTAssertFalse(surface.positions.contains { $0.x > 0.5 && $0.x < 1.5 && $0.y > 0.5 && $0.y < 1.5 })
    }

    func testIsolatedCrossingsDoNotDistortCameraBounds() throws {
        var samples = plane().samples
        for x in 100...101 {
            for y in 100...101 {
                for z in 0...1 {
                    samples.append(VolumeSample(x: x, y: y, z: z, distance: Float(z) - 0.25, views: 2))
                }
            }
        }
        let surface = try FusionSurfaceBuilder().build(FusedVolume(voxelSize: 0.5, samples: samples))
        XCTAssertEqual(surface.triangleCount, 18)
        XCTAssertEqual(surface.positions.count, 16)
        XCTAssertTrue(surface.positions.allSatisfy { $0.x < 2 && $0.y < 2 })
    }

    func testInvalidOrDuplicateVolumeIsRejected() throws {
        XCTAssertThrowsError(try FusionSurfaceBuilder().build(FusedVolume(voxelSize: 0, samples: plane().samples)))
        let source = plane()
        XCTAssertThrowsError(
            try FusionSurfaceBuilder().build(FusedVolume(voxelSize: 1, samples: source.samples + [source.samples[0]])))
    }

    func testSlicesUseExactPlaneAndWorldCoordinates() throws {
        let volume = plane()
        let bounds = try FusionVolumeBounds(volume: volume)
        let first = try FusionSliceRenderer().render(volume: volume, bounds: bounds, axis: "Z", fraction: 0)
        let last = try FusionSliceRenderer().render(volume: volume, bounds: bounds, axis: "Z", fraction: 1)
        XCTAssertEqual(first.index, -2)
        XCTAssertEqual(last.index, 2)
        XCTAssertEqual(first.count, 25)
        XCTAssertEqual(last.count, 25)
        XCTAssertEqual(first.image.size, last.image.size)
        let info = FusionPresentationInfo(
            mode: .sliceZ, samples: volume.samples.count, voxelSize: 0.5, metricScale: false)
        XCTAssertEqual(info.units, "scene units")
        XCTAssertEqual(FusionViewMode(selection: "Y"), .sliceY)
        XCTAssertEqual(FusionViewMode.allCases.first, .surface)
    }

    func testReaderUsesFullVolumeAndSupportKeepsIdenticalGeometry() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let volume = plane()
        let object: [String: Any] = [
            "voxelSize": volume.voxelSize,
            "samples": volume.samples.map {
                ["x": $0.x, "y": $0.y, "z": $0.z, "distance": $0.distance, "views": $0.views] as [String: Any]
            },
        ]
        try BinaryArtifactFixture.write(folder.appendingPathComponent("volume.json"), value: object)
        try Data("Unused sampled preview".utf8).write(to: folder.appendingPathComponent("volume.preview.json"))
        let reader = SavedFusionArtifactReader(json: ArtifactJSONReader(), scenes: NativeSceneFactory())
        let shaded = try await reader.load(folder: folder, mode: "Surface", slice: 0.5, metricScale: false)
        let support = try await reader.load(folder: folder, mode: "Support", slice: 0.5, metricScale: false)
        let a = try XCTUnwrap(shaded.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.geometry)
        let b = try XCTUnwrap(support.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.geometry)
        XCTAssertEqual(a.elements.first?.primitiveCount, 18)
        XCTAssertEqual(a.sources(for: .vertex).first?.data, b.sources(for: .vertex).first?.data)
        XCTAssertEqual(a.elements.first?.data, b.elements.first?.data)
        XCTAssertFalse(b.sources(for: .color).isEmpty)
        XCTAssertEqual(shaded.sceneIdentity, support.sceneIdentity)
        XCTAssertFalse(shaded.metricScale)
        XCTAssertEqual(shaded.fusion?.samples, 125)
        let slice = try await reader.load(folder: folder, mode: "Slice Z", slice: 0, metricScale: true)
        XCTAssertEqual(slice.fusion?.slicePosition, -1)
        XCTAssertEqual(slice.fusion?.sliceSamples, 25)
        XCTAssertEqual(slice.fusion?.units, "m")
    }

    func testCancelledExtractionStops() async throws {
        let task = Task {
            while !Task.isCancelled { await Task.yield() }
            return try FusionSurfaceBuilder().build(self.plane())
        }
        task.cancel()
        do {
            _ = try await task.value
            XCTFail("Cancelled extraction must stop")
        } catch is CancellationError {}
    }

    func testRenderSavedFusionArtifact() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_FUSION_ARTIFACT"],
            let output = environment["ECHO_TEST_RENDER_DIR"], let device = MTLCreateSystemDefaultDevice()
        else { throw XCTSkip("Set ECHO_TEST_FUSION_ARTIFACT and ECHO_TEST_RENDER_DIR for saved-volume rendering.") }
        let folder = URL(fileURLWithPath: path)
        let destination = URL(fileURLWithPath: output)
        try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        let reader = SavedFusionArtifactReader(json: ArtifactJSONReader(), scenes: NativeSceneFactory())
        var fraction = 0.5
        for mode in ["Surface", "Support", "Slice Y"] {
            let start = Date()
            let value = try await reader.load(folder: folder, mode: mode, slice: fraction, metricScale: true)
            fraction = value.fusion?.preferredSlices["Y"] ?? fraction
            let image: NSImage
            if let scene = value.scene {
                let geometry = try XCTUnwrap(
                    scene.rootNode.childNode(withName: "artifact", recursively: false)?.geometry)
                XCTAssertGreaterThan(geometry.elements.first?.primitiveCount ?? 0, 100)
                let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
                let box = geometry.boundingBox
                let bounds = SceneBounds(positions: [
                    [box.min.x, box.min.y, box.min.z], [box.max.x, box.max.y, box.max.z],
                ])
                bounds.fit(camera: camera, size: CGSize(width: 1200, height: 800))
                let renderer = SCNRenderer(device: device, options: nil)
                renderer.scene = scene
                renderer.pointOfView = camera
                renderer.autoenablesDefaultLighting = false
                image = renderer.snapshot(
                    atTime: 0, with: CGSize(width: 1200, height: 800), antialiasingMode: .multisampling4X)
            } else {
                image = try XCTUnwrap(value.image)
                XCTAssertGreaterThan(value.fusion?.sliceSamples ?? 0, 0)
            }
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: destination.appendingPathComponent("fusion-\(mode.replacingOccurrences(of: " ", with: "-")).png"))
            print("Fusion \(mode): \(Date().timeIntervalSince(start)) s; \(value.detail)")
        }
    }

    private func plane() -> FusedVolume {
        var samples: [VolumeSample] = []
        for x in 0...4 {
            for y in 0...4 {
                for z in -2...2 {
                    samples.append(VolumeSample(x: x, y: y, z: z, distance: Float(z) - 0.25, views: x < 2 ? 2 : 12))
                }
            }
        }
        return FusedVolume(voxelSize: 0.5, samples: samples)
    }
}
