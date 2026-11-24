import Foundation
import XCTest

@testable import EchoDesktop

final class RuntimeInfoTests: XCTestCase {
    func testNativeReportContainsActualProcessAndHardwareWithoutRequiringBuildMetadata() {
        let info = NativeRuntimeInfo(bundle: .main, processorURL: URL(fileURLWithPath: "/tmp/echo"))
        let sections = info.read()
        let fields = sections.flatMap(\.fields)
        XCTAssertEqual(
            fields.first { $0.name == "Process ID" }?.value, String(ProcessInfo.processInfo.processIdentifier))
        XCTAssertEqual(fields.first { $0.name == "Processor path" }?.value, "/tmp/echo")
        XCTAssertNotNil(fields.first { $0.name == "System memory" })
        XCTAssertNotNil(fields.first { $0.name == "Native build" })
        XCTAssertEqual(fields.count, Set(fields.map(\.id)).count)
    }

    func testProcessorReportDecodesRealCLIContract() async throws {
        guard let executable = ProcessInfo.processInfo.environment["ECHO_TEST_CLI"] else {
            throw XCTSkip("Set ECHO_TEST_CLI to inspect the built processor runtime")
        }
        let client = ProcessCLIClient(executable: URL(fileURLWithPath: executable))
        let data = try await client.execute(["runtime-info"], progress: { _ in })
        let info = try JSONDecoder().decode(ProcessorRuntimeInfo.self, from: data)
        XCTAssertEqual(info.schemaVersion, 1)
        XCTAssertFalse(info.framework.isEmpty)
        XCTAssertGreaterThan(info.processorCount, 0)
        XCTAssertGreaterThan(info.gcAvailableMemoryBytes, 0)
        XCTAssertNotNil(info.assemblies["echo"])
        XCTAssertEqual(info.sections[0].fields.first { $0.name == "Build" }?.value, info.buildConfiguration)
        XCTAssertEqual(
            info.sections[0].fields.first { $0.name == "Code generation" }?.value,
            info.dynamicCodeCompiled ? "JIT enabled" : "Ahead of time / interpreter (no JIT)")
    }
}
