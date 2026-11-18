import AppKit
import XCTest

@testable import EchoDesktop

final class GeoTiffTests: XCTestCase {
    func testGeoTiffPreservesPixelsAndEmbedsPixelCornerPlacement() throws {
        let bitmap = try XCTUnwrap(
            NSBitmapImageRep(
                bitmapDataPlanes: nil, pixelsWide: 16, pixelsHigh: 8, bitsPerSample: 8, samplesPerPixel: 4,
                hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        for y in 0..<8 {
            for x in 0..<16 {
                bitmap.setColor(
                    NSColor(deviceRed: y < 4 ? 1 : 0, green: 0, blue: y < 4 ? 0 : 1, alpha: 1),
                    atX: x, y: y)
            }
        }
        bitmap.setColor(NSColor(deviceRed: 0, green: 0, blue: 0, alpha: 0), atX: 0, y: 0)
        let data = try GeoTiffWriter().encode(bitmap, worldFile: [2, 0, 0, -2, 1001, 1999])
        let restored = try XCTUnwrap(NSBitmapImageRep(data: data))
        XCTAssertEqual(restored.pixelsWide, 16)
        XCTAssertEqual(restored.pixelsHigh, 8)
        XCTAssertGreaterThan(
            try XCTUnwrap(restored.colorAt(x: 8, y: 1)?.usingColorSpace(.deviceRGB)).redComponent, 0.95)
        XCTAssertGreaterThan(
            try XCTUnwrap(restored.colorAt(x: 8, y: 6)?.usingColorSpace(.deviceRGB)).blueComponent, 0.95)
        XCTAssertEqual(try XCTUnwrap(restored.colorAt(x: 0, y: 0)).alphaComponent, 0)
        let little = data[0] == 0x49
        func value(_ offset: Int, _ count: Int) -> UInt64 {
            var result: UInt64 = 0
            for index in 0..<count {
                result |= UInt64(data[offset + index]) << ((little ? index : count - 1 - index) * 8)
            }
            return result
        }
        let directory = Int(value(4, 4))
        var tags: [UInt64: Int] = [:]
        for index in 0..<Int(value(directory, 2)) {
            let entry = directory + 2 + index * 12
            tags[value(entry, 2)] = Int(value(entry + 8, 4))
        }
        let scale = try XCTUnwrap(tags[33550])
        XCTAssertEqual(Double(bitPattern: value(scale, 8)), 2)
        XCTAssertEqual(Double(bitPattern: value(scale + 8, 8)), 2)
        let tie = try XCTUnwrap(tags[33922])
        XCTAssertEqual(Double(bitPattern: value(tie + 24, 8)), 1000)
        XCTAssertEqual(Double(bitPattern: value(tie + 32, 8)), 2000)
        let keys = try XCTUnwrap(tags[34735])
        XCTAssertEqual(value(keys + 6, 2), 4)
        XCTAssertEqual(value(keys + 30, 2), 3857)
        if let output = ProcessInfo.processInfo.environment["ECHO_GIS_TEST_OUTPUT"] {
            let folder = URL(fileURLWithPath: output)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try data.write(to: folder.appendingPathComponent("model-map.tif"))
        }
    }

    func testGeoTiffRejectsInvalidRasterTransforms() throws {
        let image = try XCTUnwrap(
            NSBitmapImageRep(
                bitmapDataPlanes: nil, pixelsWide: 2, pixelsHigh: 2, bitsPerSample: 8, samplesPerPixel: 4,
                hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        XCTAssertThrowsError(try GeoTiffWriter().encode(image, worldFile: [1, 0, 0, -1, .nan, 0]))
        XCTAssertThrowsError(try GeoTiffWriter().encode(image, worldFile: [1, 1, 0, -1, 0, 0]))
    }
}
