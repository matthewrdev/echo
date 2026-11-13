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
        let map = DepthMap(imageId: "photo", width: 2, height: 1, depth: [0, 5], confidence: [0, 0.9])
        let overlay = try ArtifactImageRenderer().depth(map, confidence: false)
        let photo = NSImage(size: overlay.size)
        photo.lockFocus()
        NSColor.red.setFill()
        NSRect(origin: .zero, size: photo.size).fill()
        photo.unlockFocus()
        let data = DepthImagePresentation(identity: "same-photo", overlay: overlay, photograph: photo, map: map)
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

    func testSampleReadoutUsesExactPixelAndRejectsUnknownOrMalformedValues() throws {
        let map = DepthMap(
            imageId: "photo", width: 3, height: 2,
            depth: [12.25, 0, .nan, 45.5, -1, 90], confidence: [0.75, 0.8, 0.6, 0.9, 0.5, .nan])
        let first = try XCTUnwrap(map.sample(at: CGPoint(x: 0.99, y: 0.99)))
        XCTAssertEqual(first.depth, 12.25)
        XCTAssertEqual(first.confidence, 0.75)
        XCTAssertEqual(first.x, 0)
        XCTAssertEqual(first.y, 0)
        XCTAssertEqual(map.sample(at: CGPoint(x: 0, y: 1))?.depth, 45.5)
        for point in [CGPoint(x: 1, y: 0), CGPoint(x: 2, y: 0), CGPoint(x: 1, y: 1)] {
            let sample = try XCTUnwrap(map.sample(at: point))
            XCTAssertNil(sample.depth)
            XCTAssertNil(sample.confidence)
        }
        XCTAssertEqual(map.sample(at: CGPoint(x: 2, y: 1))?.depth, 90)
        XCTAssertNil(map.sample(at: CGPoint(x: 2, y: 1))?.confidence)
        for point in [CGPoint(x: -0.1, y: 0), CGPoint(x: 3, y: 0), CGPoint(x: 0, y: 2), CGPoint(x: CGFloat.nan, y: 0)] {
            XCTAssertNil(map.sample(at: point))
        }
        XCTAssertNil(DepthMap(imageId: "bad", width: 3, height: 2, depth: [1], confidence: []).sample(at: .zero))
    }

    @MainActor
    func testHoverCoordinatesFollowFitZoomAndResize() throws {
        let map = DepthMap(imageId: "photo", width: 200, height: 100, depth: [], confidence: [])
        let data = DepthImagePresentation(
            identity: "photo", overlay: NSImage(size: CGSize(width: 200, height: 100)), photograph: nil, map: map)
        let view = InteractiveCanvasView(frame: CGRect(x: 0, y: 0, width: 400, height: 200))
        view.renderer = DepthCanvasRenderer(data: data, overlayOpacity: 1)
        XCTAssertNil(view.imagePoint(at: .zero))
        XCTAssertEqual(view.imagePoint(at: CGPoint(x: 200, y: 100)), CGPoint(x: 100, y: 50))
        XCTAssertEqual(view.imagePoint(at: CGPoint(x: 104, y: 52)), CGPoint(x: 50, y: 25))
        view.zoom(by: 2, at: CGPoint(x: 104, y: 52))
        XCTAssertEqual(view.imagePoint(at: CGPoint(x: 104, y: 52)), CGPoint(x: 50, y: 25))
        view.fit()
        view.setFrameSize(CGSize(width: 800, height: 400))
        XCTAssertEqual(view.imagePoint(at: CGPoint(x: 208, y: 104)), CGPoint(x: 50, y: 25))
        var exited = false
        view.hoverChanged = { value in exited = value == nil }
        let event = try XCTUnwrap(
            NSEvent.mouseEvent(
                with: .mouseMoved, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: 0, context: nil,
                eventNumber: 0, clickCount: 0, pressure: 0))
        view.mouseExited(with: event)
        XCTAssertTrue(exited)
    }

    func testTooltipStaysBesidePointerAndFlipsAtEdges() {
        let bounds = CGRect(x: 0, y: 0, width: 600, height: 400)
        let size = CGSize(width: 160, height: 68)
        let normal = PointerTooltipLayout(point: CGPoint(x: 200, y: 100)).tooltipOrigin(size: size, bounds: bounds)
        XCTAssertEqual(normal, CGPoint(x: 214, y: 118))
        let edge = PointerTooltipLayout(point: CGPoint(x: 598, y: 398)).tooltipOrigin(size: size, bounds: bounds)
        XCTAssertLessThan(edge.x + size.width, 598)
        XCTAssertLessThan(edge.y + size.height, 398)
        XCTAssertTrue(bounds.contains(CGRect(origin: edge, size: size)))
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
