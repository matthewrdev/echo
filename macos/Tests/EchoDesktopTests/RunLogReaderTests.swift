import Foundation
import XCTest

@testable import EchoDesktop

final class RunLogReaderTests: XCTestCase {
    func testFeatureCheckpointUsesPhotoAndCountsWhileOldEventsRemainReadable() throws {
        let formatter = RunEventFormatter()
        let current = Data(
            #"{"sequence":1,"timestamp":"2026-11-14T07:49:34.006+11:00","type":"imageCheckpoint","payload":{"stage":"features","completed":54,"total":193,"message":"cliff.jpg · Found 800 points and 38 line segments · 54/193 images ready"}}"#
                .utf8)
        let entry = try XCTUnwrap(formatter.entry(from: current))
        XCTAssertEqual(entry.stage, "features")
        XCTAssertEqual(entry.message, "cliff.jpg · Found 800 points and 38 line segments · 54/193 images ready")
        let legacy = Data(
            #"{"sequence":2,"timestamp":"2026-11-14T07:49:34.006+11:00","type":"imageCheckpoint","payload":{"stage":"features","completed":54,"total":193}}"#
                .utf8)
        XCTAssertEqual(formatter.entry(from: legacy)?.message, "54 of 193 images processed.")
    }

    func testAppendedEventsBecomeReadableMessagesWithoutDuplicates() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let folder = root.appendingPathComponent("runs/test")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let file = folder.appendingPathComponent("events.ndjson")
        let first =
            #"{"sequence":1,"timestamp":"2026-11-13T01:00:00.000Z","type":"stageProgress","payload":{"stage":"cloud","message":"Checking image 2 of 48 · 100 points accepted"}}"#
        try Data((first + "\n").utf8).write(to: file)
        let reader = RunLogReader(formatter: RunEventFormatter())
        let initial = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(initial.count, 1)
        XCTAssertTrue(initial[0].message.contains("100 points accepted"))
        let repeated = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(repeated.count, 1)
        let second =
            #"{"sequence":2,"timestamp":"2026-11-13T01:00:03.000Z","type":"checkpoint","payload":{"stage":"cloudPreview"}}"#
        let handle = try FileHandle(forWritingTo: file)
        try handle.seekToEnd()
        try handle.write(contentsOf: Data(second.utf8))
        let incomplete = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(incomplete.count, 1)
        try handle.write(contentsOf: Data("\n".utf8))
        try handle.close()
        let appended = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(appended.count, 2)
        XCTAssertEqual(appended.last?.stage, "cloud")
        XCTAssertEqual(appended.last?.message, "Live dense-cloud preview updated.")
    }
}
