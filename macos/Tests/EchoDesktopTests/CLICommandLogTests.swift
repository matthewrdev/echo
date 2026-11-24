import Foundation
import XCTest

@testable import EchoDesktop

final class CLICommandLogTests: XCTestCase {
    func testCleanupPrunesOldHistoryWithoutResurrectingItOnNextCommand() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try Data("{}".utf8).write(to: root.appendingPathComponent("project.bin"))
        defer { try? FileManager.default.removeItem(at: root) }
        let log = CLICommandLog()
        let old = CLICommandInvocation(
            executable: URL(fileURLWithPath: "/tmp/echo"), arguments: ["--project", root.path, "run", "start"])
        await log.record(old)
        let cutoff = Date()
        let latest = CLICommandInvocation(
            executable: URL(fileURLWithPath: "/tmp/echo"),
            arguments: ["--project", root.path, "storage", "delete"])
        await log.record(latest)
        try await log.prune(root: root.path, before: cutoff)
        let after = CLICommandInvocation(
            executable: URL(fileURLWithPath: "/tmp/echo"), arguments: ["--project", root.path, "inspect"])
        await log.record(after)
        let restored = await CLICommandLog().entries(root: root.path)
        XCTAssertEqual(restored.map(\.id), [latest.id, after.id])
    }

    func testCancellationExitIsDistinctFromProcessingFailure() async throws {
        let client = ProcessCLIClient(executable: URL(fileURLWithPath: "/bin/sh"))
        do {
            _ = try await client.execute(["-c", "exit 130"], progress: { _ in })
            XCTFail("A stopped worker must report cancellation")
        } catch is CancellationError {
        }
        do {
            _ = try await client.execute(["-c", "echo 'Actual failure' >&2; exit 5"], progress: { _ in })
            XCTFail("A failed worker must still report an error")
        } catch let error as CLIError {
            XCTAssertTrue(error.message.contains("Actual failure"))
        }
    }

    func testCommandQuotingPreservesLiteralArgumentsAndExecutableLaunch() async throws {
        let values = ["", "two words", "Matt's cliff", "$(printf injected)", "semi;colon", "line\nbreak"]
        let invocation = CLICommandInvocation(
            executable: URL(fileURLWithPath: "/usr/bin/printf"), arguments: ["%s\\n"] + values)
        let shell = ProcessCLIClient(executable: URL(fileURLWithPath: "/bin/sh"))
        let output = try await shell.execute(["-c", invocation.command], progress: { _ in })
        XCTAssertEqual(String(decoding: output, as: UTF8.self), values.joined(separator: "\n") + "\n")
        let cli = CLICommandInvocation(
            executable: URL(fileURLWithPath: "/tmp/Echo App/echo"), arguments: ["run", "start"])
        XCTAssertEqual(cli.executable, "/tmp/Echo App/echo")
        XCTAssertEqual(cli.arguments, ["run", "start"])
        XCTAssertEqual(cli.command, "'/tmp/Echo App/echo' run start")
    }

    func testProcessBoundaryRecordsActualCommandsIncludingFailedLaunches() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try Data("{}".utf8).write(to: root.appendingPathComponent("project.bin"))
        defer { try? FileManager.default.removeItem(at: root) }
        let log = CLICommandLog()
        let client = ProcessCLIClient(executable: URL(fileURLWithPath: "/bin/echo"), commandLog: log)
        let arguments = ["--project", root.path, "run", "start", "--target", "mesh"]
        _ = try await client.execute(arguments, progress: { _ in })
        let missing = ProcessCLIClient(executable: root.appendingPathComponent("missing-worker"), commandLog: log)
        do {
            _ = try await missing.execute(arguments, progress: { _ in })
            XCTFail("The missing executable should fail")
        } catch {}
        let entries = await log.entries(root: root.path)
        XCTAssertEqual(entries.count, 2)
        XCTAssertEqual(entries.first?.arguments, arguments)
        XCTAssertEqual(entries.first?.executable, "/bin/echo")
        XCTAssertEqual(entries.last?.executable, root.appendingPathComponent("missing-worker").path)
        let restored = await CLICommandLog().entries(root: root.path)
        XCTAssertEqual(restored.map(\.id), entries.map(\.id))
    }

    func testCommandHistoryHasNoRecentEntryLimitAndSeparatesBackgroundQueries() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try Data("{}".utf8).write(to: root.appendingPathComponent("project.bin"))
        defer { try? FileManager.default.removeItem(at: root) }
        let log = CLICommandLog()
        for index in 0..<300 {
            await log.record(
                CLICommandInvocation(
                    executable: URL(fileURLWithPath: "/tmp/echo"),
                    arguments: ["--project", root.path, "run", "resume", "run-\(index)"]))
        }
        await log.record(
            CLICommandInvocation(
                executable: URL(fileURLWithPath: "/tmp/echo"),
                arguments: ["--project", root.path, "inspect", "--summary"]))
        let entries = await CLICommandLog().entries(root: root.path)
        XCTAssertEqual(entries.count, 301)
        XCTAssertEqual(entries.first?.arguments.last, "run-0")
        XCTAssertEqual(entries.filter { !$0.isBackgroundQuery }.count, 300)
        XCTAssertEqual(entries.last?.isBackgroundQuery, true)
        let other = await log.entries(root: root.appendingPathComponent("other.echo").path)
        XCTAssertTrue(other.isEmpty)
    }

    func testLoggingDoesNotCreateMissingProjects() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let log = CLICommandLog()
        await log.record(
            CLICommandInvocation(
                executable: URL(fileURLWithPath: "/tmp/echo"), arguments: ["--project", root.path, "inspect"]))
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.path))
        let entries = await log.entries(root: root.path)
        XCTAssertEqual(entries.count, 1)
    }
}
