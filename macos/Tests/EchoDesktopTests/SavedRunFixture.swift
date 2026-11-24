import Foundation
import XCTest

@testable import EchoDesktop

enum SavedRunFixture {
    static func read(_ root: URL, id: String) async throws -> RunData {
        let repo = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
        let cli =
            ProcessInfo.processInfo.environment["ECHO_TEST_CLI"].map { URL(fileURLWithPath: $0) }
            ?? repo.appendingPathComponent("src/Echo.Cli/bin/Release/net10.0/echo-photo")
        try XCTSkipUnless(FileManager.default.isExecutableFile(atPath: cli.path), "Build the Echo Release CLI first.")
        return try await CLIProjectClient(cli: ProcessCLIClient(executable: cli)).run(root: root.path, id: id)
    }
}
