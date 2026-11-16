import Foundation
import XCTest

@testable import EchoDesktop

final class TelemetryHistoryTests: XCTestCase {
    @MainActor
    func testHistoryDeduplicatesBoundsAndSeparatesRunsAndProjects() throws {
        let history = TelemetryHistory()
        for second in 0..<700 {
            history.record(root: "/one", runID: "run", resources: try resources(second: second))
        }
        XCTAssertEqual(history.samples.count, 240)
        history.record(root: "/one", runID: "run", resources: try resources(second: 699))
        history.record(root: "/one", runID: "run", resources: try resources(second: 698))
        XCTAssertEqual(history.samples.count, 240)
        history.record(root: "/one", runID: "run", resources: try resources(second: 2000))
        XCTAssertEqual(history.samples.count, 1)
        history.record(root: "/one", runID: "next", resources: try resources(second: 2001))
        XCTAssertEqual(history.samples.count, 1)
        history.record(root: "/two", runID: "next", resources: nil)
        XCTAssertTrue(history.samples.isEmpty)
        history.record(root: "/two", runID: nil, resources: try resources(second: 2002))
        XCTAssertTrue(history.samples.isEmpty)
    }

    func testOldResourcesAndCpuCapacityAreHonestAboutMissingMeasurements() throws {
        let old = try resources(second: 0)
        XCTAssertNil(old.processor)
        XCTAssertNotNil(old.sampledDate)
        let cpu = ProcessorProgress(cpuPercent: 250, logicalProcessors: 10, threads: nil)
        XCTAssertEqual(cpu.capacityPercent, 25)
        XCTAssertNil(ProcessorProgress(cpuPercent: nil, logicalProcessors: 10, threads: nil).capacityPercent)
        XCTAssertNil(ProcessorProgress(cpuPercent: 50, logicalProcessors: 0, threads: nil).capacityPercent)
        XCTAssertEqual(ProcessorProgress(cpuPercent: 1050, logicalProcessors: 10, threads: []).capacityPercent, 100)
    }

    func testProgressCountsHandleLargeValuesAndUnknownOrResetTotals() throws {
        var progress = try JSONDecoder().decode(
            RunProgress.self,
            from: Data(
                #"{"stage":"mesh","message":"Extracting","completed":4000000000,"total":5000000000,"unit":"cells"}"#
                    .utf8))
        XCTAssertEqual(progress.fractionCompleted, 0.8)
        XCTAssertTrue(try XCTUnwrap(progress.countSummary).hasSuffix("cells · 80%"))
        progress = try JSONDecoder().decode(
            RunProgress.self, from: Data(#"{"stage":"mesh","message":"Repairing"}"#.utf8))
        XCTAssertNil(progress.fractionCompleted)
        XCTAssertNil(progress.countSummary)
        progress = try JSONDecoder().decode(
            RunProgress.self, from: Data(#"{"stage":"mesh","message":"Repairing","completed":1,"total":0}"#.utf8))
        XCTAssertNil(progress.fractionCompleted)
    }

    private func resources(second: Int) throws -> ResourceProgress {
        let date = Date(timeIntervalSince1970: 1_800_000_000 + Double(second))
        let timestamp = ISO8601DateFormatter().string(from: date)
        return try JSONDecoder().decode(
            ResourceProgress.self,
            from: Data(
                """
                {"sampledAt":"\(timestamp)","workingSetBytes":1024,"peakWorkingSetBytes":2048,"managedBytes":100,
                "memoryBudgetBytes":4096,"cacheResidentBytes":100,"cachePeakBytes":200,"diskReadBytes":10,
                "diskWrittenBytes":20,"pageEvictions":0,"scratchBytes":0}
                """.utf8))
    }
}
