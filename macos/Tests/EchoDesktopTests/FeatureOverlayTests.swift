import AppKit
import XCTest

@testable import EchoDesktop

final class FeatureOverlayTests: XCTestCase {
    @MainActor
    func testVisibilityControlsDrawingAndSelectionIndependently() throws {
        let data = fixture()
        let hidden = FeatureOverlayOptions(opacity: 0)
        let photograph = try draw(data, overlay: hidden)
        let options = [
            FeatureOverlayOptions(showsPoints: true, showsLines: false, showsJunctions: false),
            FeatureOverlayOptions(showsPoints: false, showsLines: false, showsJunctions: true),
            FeatureOverlayOptions(showsPoints: false, showsLines: true, showsJunctions: false),
        ]
        let positions = [CGPoint(x: 20, y: 20), CGPoint(x: 50, y: 20), CGPoint(x: 75, y: 70)]
        for (index, option) in options.enumerated() {
            var selected: [Int] = []
            let renderer = FeatureCanvasRenderer(data: data, selected: nil, overlay: option) { selected.append($0) }
            for position in positions { renderer.select(at: position, tolerance: 8) }
            XCTAssertEqual(selected, [index])
            XCTAssertNotEqual(try draw(data, overlay: option), photograph)
        }
        let noLayers = FeatureOverlayOptions(showsPoints: false, showsLines: false, showsJunctions: false)
        XCTAssertEqual(try draw(data, overlay: noLayers), photograph)
        for option in [hidden, noLayers] {
            let renderer = FeatureCanvasRenderer(data: data, selected: nil, overlay: option) { _ in
                XCTFail("Hidden overlays must not receive clicks")
            }
            for position in positions { renderer.select(at: position, tolerance: 8) }
        }
    }

    @MainActor
    func testOpacityCompositesWholeOverlayAndKeepsViewportIdentity() throws {
        let data = fixture()
        let hidden = try draw(data, overlay: FeatureOverlayOptions(opacity: 0))
        let half = try draw(data, overlay: FeatureOverlayOptions(opacity: 0.5))
        let full = try draw(data, overlay: FeatureOverlayOptions(opacity: 1))
        var changed = 0
        for index in hidden.indices where index % 4 != 3 {
            let expected = (Double(hidden[index]) + Double(full[index])) / 2
            XCTAssertEqual(Double(half[index]), expected, accuracy: 2)
            if full[index] != hidden[index] { changed += 1 }
        }
        XCTAssertGreaterThan(changed, 100)
        let renderer = FeatureCanvasRenderer(data: data, selected: nil) { _ in }
        let faded = FeatureCanvasRenderer(data: data, selected: nil, overlay: FeatureOverlayOptions(opacity: 0.5)) {
            _ in
        }
        XCTAssertEqual(renderer.identity, faded.identity)
        let canvas = InteractiveCanvasView(frame: CGRect(x: 0, y: 0, width: 400, height: 400))
        canvas.renderer = renderer
        canvas.zoom(by: 2, at: CGPoint(x: 100, y: 100))
        let point = canvas.imagePoint(at: CGPoint(x: 100, y: 100))
        canvas.renderer = faded
        XCTAssertEqual(canvas.imagePoint(at: CGPoint(x: 100, y: 100)), point)
    }

    @MainActor
    func testSelectionFadesOtherFeaturesButKeepsSelectedLineProminent() throws {
        let data = fixture()
        let photograph = try draw(data, overlay: FeatureOverlayOptions(opacity: 0))
        let faded = try draw(data, overlay: FeatureOverlayOptions(opacity: 0.15))
        let focused = try draw(data, overlay: FeatureOverlayOptions(), selected: 2)
        let lineOnly = try draw(
            data, overlay: FeatureOverlayOptions(showsPoints: false, showsJunctions: false), selected: 2)
        var fadedPixels = 0
        var selectedPixels = 0
        for offset in stride(from: 0, to: photograph.count, by: 4) {
            let pixel = offset..<offset + 3
            let selectedVisible = lineOnly[pixel] != photograph[pixel]
            XCTAssertEqual(focused[pixel], selectedVisible ? lineOnly[pixel] : faded[pixel])
            if selectedVisible { selectedPixels += 1 }
            if !selectedVisible, faded[pixel] != photograph[pixel] { fadedPixels += 1 }
        }
        XCTAssertGreaterThan(fadedPixels, 50)
        XCTAssertGreaterThan(selectedPixels, 50)
        let hiddenLine = FeatureOverlayOptions(showsLines: false)
        XCTAssertEqual(try draw(data, overlay: hiddenLine, selected: 2), try draw(data, overlay: hiddenLine))
    }

    @MainActor
    private func fixture() -> FeaturePresentation {
        let image = NSImage(size: CGSize(width: 100, height: 100))
        image.lockFocus()
        NSColor.white.setFill()
        NSRect(x: 0, y: 0, width: 100, height: 100).fill()
        image.unlockFocus()
        return FeaturePresentation(
            photo: Photo(
                id: "photo", name: "photo", source: "", preview: "", width: 100, height: 100,
                capturedAt: nil, camera: "", focalPixels: 100, enabled: true, sets: [], gps: nil,
                attitude: nil, metadata: [:]),
            image: image,
            features: [
                ImageFeature(x: 20, y: 20, descriptor: []),
                ImageFeature(x: 50, y: 20, kind: "junction", descriptor: []),
            ],
            matches: [], matchingAvailable: false,
            lines: [LineFeature(start: [60, 70], end: [90, 70], descriptor: [], strength: 1)])
    }

    @MainActor
    private func draw(_ data: FeaturePresentation, overlay: FeatureOverlayOptions, selected: Int? = nil) throws
        -> [UInt8]
    {
        let context = try XCTUnwrap(
            CGContext(
                data: nil, width: 100, height: 100, bitsPerComponent: 8, bytesPerRow: 400,
                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
        FeatureCanvasRenderer(data: data, selected: selected, overlay: overlay) { _ in }.draw(in: context, scale: 1)
        NSGraphicsContext.restoreGraphicsState()
        let bytes = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
        return Array(UnsafeBufferPointer(start: bytes, count: 40000))
    }
}
