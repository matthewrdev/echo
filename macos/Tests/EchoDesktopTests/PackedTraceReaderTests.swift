import Foundation
import XCTest

@testable import EchoDesktop

final class PackedTraceReaderTests: XCTestCase {
    func testReadsCommittedSpansAndTailsLaterFrames() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let run = String(repeating: "a", count: 32)
        let attempt = String(repeating: "b", count: 32)
        let directory = root.appendingPathComponent("runs/\(run)/traces")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let header = directory.appendingPathComponent("\(attempt).trace.bin")
        let journal = directory.appendingPathComponent("\(attempt).trace.spans.bin")
        var frames = PackedFixture.header(0x3153_5445)
        frames.append(span(2))
        let committed = frames.count
        frames.append(span(1))
        try frames.write(to: journal)
        try summary(run: run, attempt: attempt, committed: committed, tick: 0).write(to: header, options: .atomic)

        let reader = PackedTraceReader()
        let list = try await reader.list(root: root.path)
        let first = try await reader.document(root: root.path, run: run, attempt: attempt)
        XCTAssertEqual(first.summary.updatedAt, list.first?.updatedAt)
        XCTAssertEqual(first.spans.map(\.id), [2])

        try summary(run: run, attempt: attempt, committed: frames.count, tick: 1).write(to: header, options: .atomic)
        let second = try await reader.document(root: root.path, run: run, attempt: attempt)
        XCTAssertEqual(second.spans.map(\.id), [1, 2])
        XCTAssertNotEqual(second.summary.updatedAt, first.summary.updatedAt)
    }

    private func summary(run: String, attempt: String, committed: Int, tick: UInt64) -> Data {
        var bytes = PackedFixture.header(0x3148_5445)
        PackedFixture.append(UInt64(0), to: &bytes)
        bytes.append(0)
        PackedFixture.append(attempt, to: &bytes)
        PackedFixture.append(run, to: &bytes)
        PackedFixture.append("features", to: &bytes)
        PackedFixture.append(UInt64(639_303_012_000_000_000), to: &bytes)
        PackedFixture.append(639_303_012_000_000_000 + tick, to: &bytes)
        bytes.append(1)
        for _ in 0..<3 { PackedFixture.append(UInt64(0), to: &bytes) }
        PackedFixture.append(UInt64(committed), to: &bytes)
        for _ in 0..<3 { PackedFixture.append(UInt32(0), to: &bytes) }
        return bytes
    }

    private func span(_ id: UInt64) -> Data {
        var bytes = Data(repeating: 0, count: 7)
        PackedFixture.append(id, to: &bytes)
        bytes.append(0)
        PackedFixture.append("features", to: &bytes)
        PackedFixture.append("checkpoint", to: &bytes)
        bytes.append(contentsOf: [1, 0, 1])
        PackedFixture.append(UInt32(0), to: &bytes)
        PackedFixture.append(UInt64(0), to: &bytes)
        PackedFixture.append(Double(12).bitPattern, to: &bytes)
        bytes.append(contentsOf: [0, 0, 0])
        PackedFixture.append("", to: &bytes)
        PackedFixture.append(UInt64(0), to: &bytes)
        bytes.append(0)
        return PackedFixture.frame(bytes)
    }
}
