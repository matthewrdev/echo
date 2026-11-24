import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class PerformanceGraphTests: XCTestCase {
    @MainActor
    func testGraphIncludesEveryStageAndDistinguishesReuseFromNewWork() throws {
        let stages = PipelineStage.allCases
        let spans: [[String: Any]] = stages.enumerated().map { index, stage in
            [
                "id": index + 1, "name": stage.rawValue, "stage": stage.rawValue, "kind": "stage", "backend": "mixed",
                "status": "completed", "threadId": 1, "startMilliseconds": 100 + index * 1000,
                "durationMilliseconds": 1000, "unit": "items", "bytes": 0,
            ]
        }
        let statistics: [[String: Any]] = stages.enumerated().map { index, stage in
            [
                "stage": stage.rawValue, "name": "units", "kind": "checkpoint", "backend": "mixed", "count": 10,
                "reusedCount": index == 0 ? 10 : 2, "failedCount": 0, "totalMilliseconds": 1000,
                "maximumMilliseconds": 100, "items": 0, "bytes": 0,
            ]
        }
        let object: [String: Any] = [
            "schemaVersion": 1, "spans": spans, "statistics": statistics,
            "summary": [
                "id": "test", "runId": "run", "target": "export", "startedAt": "", "updatedAt": "",
                "status": "completed", "durationMilliseconds": 9100, "cpuMilliseconds": 12000, "droppedSpans": 1000,
            ],
        ]
        let document = try JSONDecoder().decode(
            PerformanceTraceDocument.self, from: JSONSerialization.data(withJSONObject: object))
        let nodes = PerformanceGraphNode.make(document)
        XCTAssertEqual(nodes.count, stages.count + 2)
        XCTAssertEqual(nodes.compactMap(\.stage), stages)
        XCTAssertEqual(nodes[1].status, .reused)
        XCTAssertEqual(nodes[2].detail, "8 built · 2 reused")
        XCTAssertEqual(nodes.last?.title, "Requested output ready")
        guard WindowedTests.isEnabled, let output = ProcessInfo.processInfo.environment["ECHO_TEST_RENDER_DIR"] else {
            return
        }
        let view = NSHostingView(
            rootView: PerformanceRunGraph(document: document, select: { _ in })
                .padding(20).frame(width: 1100, height: 900).background(Color.white).environment(\.colorScheme, .light))
        view.frame = NSRect(x: 0, y: 0, width: 1100, height: 900)
        let window = NSWindow(contentRect: view.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = view
        defer { window.contentView = nil }
        view.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
            to: URL(fileURLWithPath: output).appendingPathComponent("performance-full-graph-fixture.png"))
    }
}
