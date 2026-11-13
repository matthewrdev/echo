import AppKit
import XCTest

@testable import EchoDesktop

final class FeaturePatchTests: XCTestCase {
    func testLongLinePreviewIncludesBothEndpointsAfterResizingAndClipping() throws {
        let image = NSImage(size: NSSize(width: 500, height: 300))
        image.lockFocus()
        NSColor.gray.setFill()
        NSRect(x: 0, y: 0, width: 500, height: 300).fill()
        image.unlockFocus()
        let patch = try XCTUnwrap(
            FeaturePatchBuilder().build(
                image: image, photoSize: CGSize(width: 2000, height: 1200),
                pixel: CGPoint(x: 40, y: 500),
                overlay: .line(start: CGPoint(x: 30, y: 100), end: CGPoint(x: 50, y: 900))))
        guard case .line(let start, let end) = patch.overlay else { return XCTFail("Line geometry was lost") }
        let bounds = CGRect(origin: .zero, size: patch.image.size)
        XCTAssertTrue(bounds.contains(start))
        XCTAssertTrue(bounds.contains(end))
        XCTAssertEqual((start.x + end.x) / 2, patch.centre.x, accuracy: 0.001)
        XCTAssertEqual((start.y + end.y) / 2, patch.centre.y, accuracy: 0.001)
        XCTAssertGreaterThan(hypot(end.x - start.x, end.y - start.y), 100)
    }

    func testJunctionTypeSurvivesCropTransform() {
        let overlay = FeaturePatchOverlay.point(kind: "junction").transformed(
            scaleX: 0.25, scaleY: 0.25, origin: CGPoint(x: 20, y: 30))
        XCTAssertEqual(overlay.title, "Line junction")
        XCTAssertEqual(FeaturePatchOverlay(line: [[1], [2]]).title, "Point feature")
    }
}
