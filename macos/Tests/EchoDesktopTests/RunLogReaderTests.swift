import Foundation
import XCTest

@testable import EchoDesktop

final class RunLogReaderTests: XCTestCase {
    func testFullHistorySurvivesPollingAndReaderRecreation() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let folder = root.appendingPathComponent("runs/test")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let file = folder.appendingPathComponent("events.bin")
        let frames = (1...1200).map { index in
            PackedFixture.event(index, type: "stageProgress", fields: ["stage": "mesh", "message": "Message \(index)"])
        }
        let header = PackedFixture.header(0x3156_4545)
        var fileBytes = header
        for frame in frames { fileBytes.append(frame) }
        try fileBytes.write(to: file)
        let reader = RunLogReader(formatter: RunEventFormatter())
        for _ in 0..<2 {
            let entries = try await reader.read(root: root.path, runID: "test")
            XCTAssertEqual(entries.count, 1200)
            XCTAssertEqual(entries.first?.message, "Message 1")
            XCTAssertEqual(entries.last?.message, "Message 1200")
        }
        let reopened = try await RunLogReader(formatter: RunEventFormatter()).read(root: root.path, runID: "test")
        XCTAssertEqual(reopened.count, 1200)
        try (header + frames[0]).write(to: file)
        let truncated = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(truncated.count, 1)
        XCTAssertEqual(truncated[0].message, "Message 1")
    }

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
        let file = folder.appendingPathComponent("events.bin")
        let first = PackedFixture.event(
            1, type: "stageProgress",
            fields: ["stage": "cloud", "message": "Checking image 2 of 48 · 100 points accepted"])
        try (PackedFixture.header(0x3156_4545) + first).write(to: file)
        let reader = RunLogReader(formatter: RunEventFormatter())
        let initial = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(initial.count, 1)
        XCTAssertTrue(initial[0].message.contains("100 points accepted"))
        let repeated = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(repeated.count, 1)
        let second = PackedFixture.event(2, type: "checkpoint", fields: ["stage": "cloudPreview"])
        let handle = try FileHandle(forWritingTo: file)
        try handle.seekToEnd()
        try handle.write(contentsOf: second.dropLast())
        let incomplete = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(incomplete.count, 1)
        try handle.write(contentsOf: second.suffix(1))
        try handle.close()
        let appended = try await reader.read(root: root.path, runID: "test")
        XCTAssertEqual(appended.count, 2)
        XCTAssertEqual(appended.last?.stage, "cloud")
        XCTAssertEqual(appended.last?.message, "Live dense-cloud preview updated.")
    }
}
