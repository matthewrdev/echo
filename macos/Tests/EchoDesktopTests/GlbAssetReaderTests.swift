import AppKit
import EchoRendering
import XCTest

@testable import EchoDesktop

final class GlbAssetReaderTests: XCTestCase {
    func testBaseColourFollowsPrimitiveMaterialAndTextureSource() throws {
        let file = try GlbAssetFixture().make(textureSource: 1)
        defer { try? FileManager.default.removeItem(at: file) }
        let asset = try GlbAssetReader().read(file)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(asset.image.tiffRepresentation)))
        let colour = try XCTUnwrap(bitmap.colorAt(x: 0, y: 0)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(colour.redComponent, 0.9)
        XCTAssertGreaterThan(colour.greenComponent, 0.9)
        XCTAssertLessThan(colour.blueComponent, 0.1)
        XCTAssertEqual(asset.surface.faces.count, 1)
    }

    func testInvalidBaseColourImageDoesNotFallBackToNormalMap() throws {
        let file = try GlbAssetFixture().make(textureSource: 99)
        defer { try? FileManager.default.removeItem(at: file) }
        XCTAssertThrowsError(try GlbAssetReader().read(file))
    }

}
