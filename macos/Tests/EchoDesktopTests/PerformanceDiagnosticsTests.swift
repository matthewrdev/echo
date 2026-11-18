import Foundation
import XCTest

@testable import EchoDesktop

final class PerformanceDiagnosticsTests: XCTestCase {
    func testOlderTraceLeavesNewDiagnosticsAbsent() throws {
        let trace = try PerformanceTraceFixture.document(id: "older", run: "run", second: 1, duration: 1000)
        XCTAssertNil(trace.measurements)
        XCTAssertNil(trace.summary.environment)
        XCTAssertNil(trace.statistics.first?.busyMilliseconds)
        XCTAssertNil(trace.spans.first?.threadAllocatedBytes)
    }

    func testDetailedDiagnosticsSurviveNativeDecodeAndExport() throws {
        let fixture = try PerformanceTraceFixture.document(id: "new", run: "run", second: 1, duration: 1000)
        var object = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(fixture)) as? [String: Any])
        var summary = try XCTUnwrap(object["summary"] as? [String: Any])
        summary["environment"] = [
            "runtime": ".NET 10", "operatingSystem": "macOS", "architecture": "Arm64",
            "logicalProcessors": 18, "serverGc": false,
        ]
        summary["droppedMeasurements"] = 5
        object["summary"] = summary
        var statistics = try XCTUnwrap(object["statistics"] as? [[String: Any]])
        statistics[0]["busyMilliseconds"] = 600
        statistics[0]["maximumConcurrency"] = 3
        statistics[0]["p95UpperMilliseconds"] = 131.072
        statistics[0]["processAllocatedBytes"] = 1_000_000
        statistics[0]["threadAllocationSamples"] = 10
        statistics[0]["processCpuMilliseconds"] = 400
        statistics[0]["gcPauseMilliseconds"] = 2
        object["statistics"] = statistics
        var spans = try XCTUnwrap(object["spans"] as? [[String: Any]])
        spans[0]["threadAllocatedBytes"] = 2000
        object["spans"] = spans
        object["measurements"] = [
            ["stage": "depth", "name": "lock wait", "unit": "ms", "count": 2, "total": 8, "minimum": 2, "maximum": 6]
        ]
        let decoded = try JSONDecoder().decode(
            PerformanceTraceDocument.self, from: JSONSerialization.data(withJSONObject: object))
        let exported = try JSONDecoder().decode(PerformanceTraceDocument.self, from: JSONEncoder().encode(decoded))
        XCTAssertEqual(exported.summary.environment?.logicalProcessors, 18)
        XCTAssertEqual(exported.summary.droppedMeasurements, 5)
        XCTAssertEqual(exported.statistics[0].busyMilliseconds, 600)
        XCTAssertEqual(exported.statistics[0].maximumConcurrency, 3)
        XCTAssertEqual(exported.statistics[0].p95UpperMilliseconds, 131.072)
        XCTAssertEqual(exported.statistics[0].processAllocatedBytes, 1_000_000)
        XCTAssertEqual(exported.statistics[0].processCpuMilliseconds, 400)
        XCTAssertEqual(exported.statistics[0].gcPauseMilliseconds, 2)
        XCTAssertEqual(exported.spans[0].threadAllocatedBytes, 2000)
        XCTAssertEqual(exported.measurements?.first?.mean, 4)
    }
}
