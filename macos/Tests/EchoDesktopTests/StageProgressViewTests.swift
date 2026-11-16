import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class StageProgressViewTests: XCTestCase {
    @MainActor
    func testMeshProgressRendersBeforeSimplificationAndWithNestedSteps() async throws {
        let data = Data(
            #"{"stage":"mesh","message":"Loading","stageSteps":["Load inputs","Extract surface","Simplify geometry","Repair observed holes","Complete surface","Repair final holes","Select photographs","Save mesh"],"stageStepIndex":0}"#
                .utf8)
        let initial = try JSONDecoder().decode(RunProgress.self, from: data)
        var nested = initial
        nested.stageStepIndex = 2
        nested.steps = [
            "Prepare vertices", "Prepare triangles", "Measure error", "Index edges", "Constrain boundaries",
            "Rank candidates", "Collapse edges", "Compact mesh",
        ]
        nested.stepIndex = 6
        let view = NSHostingView(
            rootView:
                VStack(alignment: .leading, spacing: 24) {
                    Text("Mesh · Loading inputs").font(.headline)
                    StageProgressBar(progress: initial, showsLegend: true)
                    Divider()
                    Text("Mesh · Simplifying geometry").font(.headline)
                    StageProgressBar(progress: nested, showsLegend: true)
                }.padding(20).frame(width: 1100, height: 260).background(Color.white).environment(\.colorScheme, .light)
        )
        view.frame = NSRect(x: 0, y: 0, width: 1100, height: 260)
        let window = NSWindow(contentRect: view.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = view
        defer { window.contentView = nil }
        view.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        XCTAssertGreaterThan(bitmap.pixelsWide, 0)
        if let output = ProcessInfo.processInfo.environment["ECHO_TEST_RENDER_DIR"] {
            try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: URL(fileURLWithPath: output).appendingPathComponent("mesh-progress.png"))
        }
    }
}
