import AppKit
import EchoRendering
import MapKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class GeoreferenceTests: XCTestCase {
    func testGeographicTransformPreservesAxesAndHeight() throws {
        let origin = GeoPosition(latitude: -33.7, longitude: 150, altitude: 123, altitudeDatum: "unknown")
        let transform = GeographicTransform(origin: origin)
        let target = GeoPosition(latitude: -33.699, longitude: 150.002, altitude: 153)
        let local = transform.local(target)
        XCTAssertGreaterThan(local.x, 0)
        XCTAssertLessThan(local.z, 0)
        let restored = transform.geographic([local.x, local.y, local.z])
        XCTAssertEqual(restored.latitude, target.latitude, accuracy: 1e-9)
        XCTAssertEqual(restored.longitude, target.longitude, accuracy: 1e-9)
        XCTAssertEqual(try XCTUnwrap(restored.altitude), 153, accuracy: 1e-6)
        XCTAssertEqual(restored.altitudeDatum, "unknown")
        let unmeasured = GeographicTransform(origin: GeoPosition(latitude: 0, longitude: 0))
        XCTAssertNil(unmeasured.geographic([0, 12, 0]).altitude)
    }

    func testWorldFileUsesMercatorMetresAndPixelCentres() {
        let centre = MKMapPoint(CLLocationCoordinate2D(latitude: 0, longitude: 0))
        let scale = MKMapRect.world.width / (2 * Double.pi * 6_378_137)
        let rect = MKMapRect(
            x: centre.x - 50 * scale, y: centre.y - 50 * scale, width: 100 * scale, height: 100 * scale)
        let file = GeographicRasterWriter().worldFile(rect: rect, width: 100, height: 100)
        for (actual, expected) in zip(file, [1.0, 0, 0, -1, -49.5, 49.5]) {
            XCTAssertEqual(actual, expected, accuracy: 1e-7)
        }
    }

    func testModelProjectionIsNorthUpAndMatchesItsGeographicBounds() throws {
        guard MTLCreateSystemDefaultDevice() != nil else { throw XCTSkip("Requires Metal") }
        let bitmap = try XCTUnwrap(
            NSBitmapImageRep(
                bitmapDataPlanes: nil, pixelsWide: 64, pixelsHigh: 64, bitsPerSample: 8, samplesPerPixel: 4,
                hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        for y in 0..<64 {
            for x in 0..<64 {
                bitmap.setColor(
                    NSColor(deviceRed: y < 32 ? 1 : 0, green: 0, blue: y < 32 ? 0 : 1, alpha: 1), atX: x, y: y)
            }
        }
        let texture = NSImage(size: CGSize(width: 64, height: 64))
        texture.addRepresentation(bitmap)
        let surface = Surface(
            vertices: [[-10, 0, -10], [10, 0, -10], [10, 0, 10], [-10, 0, 10]],
            faces: [[0, 1, 2], [0, 2, 3]], sourceImages: [])
        let overlay = try ModelMapProjector().render(
            surface: surface, uvs: [[0, 0], [1, 0], [1, 1], [0, 1]], texture: texture,
            origin: GeoPosition(latitude: -33, longitude: 150, altitude: 100), size: 128)
        let rendered = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(overlay.image.tiffRepresentation)))
        let north = try XCTUnwrap(rendered.colorAt(x: 64, y: 32)?.usingColorSpace(.deviceRGB))
        let south = try XCTUnwrap(rendered.colorAt(x: 64, y: 96)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(north.redComponent, 0.8)
        XCTAssertGreaterThan(south.blueComponent, 0.8)
        XCTAssertLessThan(try XCTUnwrap(rendered.colorAt(x: 0, y: 0)).alphaComponent, 0.01)
        XCTAssertEqual(overlay.coordinate.latitude, -33, accuracy: 0.000001)
        XCTAssertEqual(overlay.coordinate.longitude, 150, accuracy: 0.000001)
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(
            "echo-geographic-raster-\(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        try GeographicRasterWriter().write(overlay, directory: directory)
        XCTAssertTrue(FileManager.default.fileExists(atPath: directory.appendingPathComponent("model-map.pgw").path))
    }

    func testSavedGeographicArtifact() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_PROJECT"], let runID = environment["ECHO_TEST_RUN"] else {
            throw XCTSkip("Set ECHO_TEST_PROJECT and ECHO_TEST_RUN to inspect a saved model")
        }
        let root = URL(fileURLWithPath: project)
        let run = try await SavedRunFixture.read(root, id: runID)
        let artifact = try GeoreferenceArtifactReader().load(root: root, run: run)
        XCTAssertNotNil(artifact.overlay)
        XCTAssertNotNil(artifact.presentation.scene)
        XCTAssertFalse(artifact.cameras.isEmpty)
        if let output = environment["ECHO_GEO_REVIEW_OUTPUT"], let overlay = artifact.overlay {
            let folder = URL(fileURLWithPath: output)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try GeographicRasterWriter().write(overlay, directory: folder)
        }
    }
}
