import AppKit
import XCTest

@testable import EchoDesktop

final class MatchOverlayTests: XCTestCase {
    @MainActor
    func testFiltersApplyToBothPhotosAndLineHitTesting() throws {
        let data = fixture()
        let options = [
            FeatureOverlayOptions(showsPoints: true, showsLines: false, showsJunctions: false),
            FeatureOverlayOptions(showsPoints: false, showsLines: false, showsJunctions: true),
            FeatureOverlayOptions(showsPoints: false, showsLines: true, showsJunctions: false),
        ]
        let positions = [CGPoint(x: 180, y: 225), CGPoint(x: 450, y: 495), CGPoint(x: 675, y: 675)]
        for (index, option) in options.enumerated() {
            var selected: [Int] = []
            let renderer = MatchCanvasRenderer(data: data, selected: nil, overlay: option) { selected.append($0) }
            for xOffset in [0.0, 990.0] {
                for position in positions {
                    renderer.select(at: CGPoint(x: position.x + xOffset, y: position.y), tolerance: 8)
                }
                renderer.select(at: CGPoint(x: 550 + xOffset, y: 675), tolerance: 8)
            }
            XCTAssertEqual(selected, index == 2 ? [2, 2, 2, 2] : [index, index])
        }
        let manual = ImageMatch(a: [0, 0], b: [0, 0], score: nil, manual: true)
        XCTAssertTrue(options[0].shows(manual))
        XCTAssertFalse(options[2].shows(manual))
        for option in [
            FeatureOverlayOptions(opacity: 0),
            FeatureOverlayOptions(showsPoints: false, showsLines: false, showsJunctions: false),
        ] {
            let renderer = MatchCanvasRenderer(data: data, selected: nil, overlay: option) { _ in
                XCTFail("Hidden matches must not receive clicks")
            }
            for position in positions { renderer.select(at: position, tolerance: 8) }
        }
    }

    @MainActor
    func testOpacityFadesMarkersAndDottedConnectionWithoutFadingPhotographs() throws {
        let data = fixture()
        let hidden = try draw(data, opacity: 0)
        let half = try draw(data, opacity: 0.5)
        let full = try draw(data, opacity: 1)
        var changed = 0
        for index in hidden.indices where index % 4 != 3 {
            XCTAssertEqual(Double(half[index]), (Double(hidden[index]) + Double(full[index])) / 2, accuracy: 2)
            if full[index] != hidden[index] { changed += 1 }
        }
        XCTAssertGreaterThan(changed, 100)
        let row = (80..<160).map { x in
            let offset = ((189 - 45 - 1) * 378 + x) * 4
            return full[offset..<offset + 3] != hidden[offset..<offset + 3]
        }
        XCTAssertGreaterThan(row.filter { $0 }.count, 20, "The selected correspondence has visible dots")
        XCTAssertGreaterThan(row.filter { !$0 }.count, 10, "The connection has clear gaps between dots")
        let renderer = MatchCanvasRenderer(data: data, selected: nil) { _ in }
        let faded = MatchCanvasRenderer(data: data, selected: nil, overlay: FeatureOverlayOptions(opacity: 0.5)) {
            _ in
        }
        XCTAssertEqual(renderer.identity, faded.identity)
        let canvas = InteractiveCanvasView(frame: CGRect(x: 0, y: 0, width: 800, height: 400))
        canvas.renderer = renderer
        canvas.zoom(by: 2, at: CGPoint(x: 200, y: 200))
        let point = canvas.imagePoint(at: CGPoint(x: 200, y: 200))
        canvas.renderer = faded
        XCTAssertEqual(canvas.imagePoint(at: CGPoint(x: 200, y: 200)), point)
    }

    @MainActor
    private func fixture() -> MatchPresentation {
        let image = NSImage(size: CGSize(width: 100, height: 100))
        image.lockFocus()
        NSColor.white.setFill()
        NSRect(x: 0, y: 0, width: 100, height: 100).fill()
        image.unlockFocus()
        func photo(_ id: String) -> Photo {
            Photo(
                id: id, name: id, source: "", preview: "", width: 100, height: 100,
                capturedAt: nil, camera: "", focalPixels: 100, enabled: true, sets: [], gps: nil,
                attitude: nil, metadata: [:])
        }
        return MatchPresentation(
            first: photo("a"), second: photo("b"), firstImage: image, secondImage: image,
            matches: [
                ImageMatch(a: [20, 20], b: [20, 20], score: 0, manual: false),
                ImageMatch(a: [50, 50], b: [50, 50], score: 0, manual: false, kind: "junction"),
                LineMatch(
                    indexA: 0, indexB: 0, startA: [60, 70], endA: [90, 70], startB: [60, 70], endB: [90, 70],
                    score: 0
                ).displayMatch,
            ])
    }

    @MainActor
    private func draw(_ data: MatchPresentation, opacity: Double) throws -> [UInt8] {
        let context = try XCTUnwrap(
            CGContext(
                data: nil, width: 378, height: 189, bitsPerComponent: 8, bytesPerRow: 1512,
                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(NSColor.white.cgColor)
        context.fill(CGRect(x: 0, y: 0, width: 378, height: 189))
        context.scaleBy(x: 0.2, y: 0.2)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
        MatchCanvasRenderer(data: data, selected: 0, overlay: FeatureOverlayOptions(opacity: opacity)) { _ in }
            .draw(in: context, scale: 0.2)
        NSGraphicsContext.restoreGraphicsState()
        let bytes = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
        return Array(UnsafeBufferPointer(start: bytes, count: 1512 * 189))
    }
}
