import AppKit
import XCTest

@testable import EchoDesktop

final class DepthImageTests: XCTestCase {
    func testMissingSamplesAreTransparentInBothViewsAndBadDimensionsFail() throws {
        let renderer = ArtifactImageRenderer()
        let map = DepthMap(imageId: "photo", width: 3, height: 1, depth: [0, 5, .nan], confidence: [0, 0.9, 0])
        for confidence in [false, true] {
            let image = try renderer.depth(map, confidence: confidence)
            var rect = CGRect(origin: .zero, size: image.size)
            let bitmap = NSBitmapImageRep(
                cgImage: try XCTUnwrap(image.cgImage(forProposedRect: &rect, context: nil, hints: nil)))
            XCTAssertEqual(try XCTUnwrap(bitmap.colorAt(x: 0, y: 0)).alphaComponent, 0)
            XCTAssertEqual(try XCTUnwrap(bitmap.colorAt(x: 1, y: 0)).alphaComponent, 1)
            XCTAssertEqual(try XCTUnwrap(bitmap.colorAt(x: 2, y: 0)).alphaComponent, 0)
        }
        XCTAssertThrowsError(
            try renderer.depth(
                DepthMap(imageId: "bad", width: 3, height: 2, depth: [1], confidence: []), confidence: true))
    }

    @MainActor
    func testOverlayFadeKeepsPhotoInMissingPixelsAndPreservesViewportIdentity() throws {
        let overlay = try ArtifactImageRenderer().depth(
            DepthMap(imageId: "photo", width: 2, height: 1, depth: [0, 5], confidence: [0, 0.9]), confidence: false)
        let photo = NSImage(size: overlay.size)
        photo.lockFocus()
        NSColor.red.setFill()
        NSRect(origin: .zero, size: photo.size).fill()
        photo.unlockFocus()
        let data = DepthImagePresentation(identity: "same-photo", overlay: overlay, photograph: photo)
        let hidden = DepthCanvasRenderer(data: data, overlayOpacity: 0)
        let half = DepthCanvasRenderer(data: data, overlayOpacity: 0.5)
        let visible = DepthCanvasRenderer(data: data, overlayOpacity: 1)
        XCTAssertEqual(hidden.identity, visible.identity)
        let before = try draw(hidden)
        let middle = try draw(half)
        let after = try draw(visible)
        XCTAssertGreaterThan(before[4], middle[4])
        XCTAssertGreaterThan(middle[4], after[4])
        XCTAssertEqual(Array(before[0..<4]), Array(after[0..<4]))
    }

    @MainActor
    private func draw(_ renderer: DepthCanvasRenderer) throws -> [UInt8] {
        let context = try XCTUnwrap(
            CGContext(
                data: nil, width: 2, height: 1, bitsPerComponent: 8, bytesPerRow: 8,
                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
        renderer.draw(in: context, scale: 1)
        NSGraphicsContext.restoreGraphicsState()
        let bytes = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
        return Array(UnsafeBufferPointer(start: bytes, count: 8))
    }
}
