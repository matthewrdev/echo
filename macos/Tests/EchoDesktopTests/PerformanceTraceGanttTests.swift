import Foundation
import JavaScriptCore
import XCTest

final class PerformanceTraceGanttTests: XCTestCase {
    func testParallelCallsUseIntervalUnionAndKeepTheirOffsets() throws {
        let result = try evaluate(
            """
            const trace = {spans: [stage(0,100), call(2,10,30), call(3,20,30), call(4,70,10)], statistics: [stat(3,70)]};
            const step = buildStageOverview(trace)[0];
            return buildSubstepGantt(trace, step).rows[0];
            """)
        XCTAssertEqual(result["activeTime"] as? Double, 50)
        XCTAssertEqual(result["workerTime"] as? Double, 70)
        XCTAssertEqual(result["complete"] as? Bool, true)
        let intervals = try XCTUnwrap(result["segments"] as? [[String: Double]])
        XCTAssertEqual(intervals.count, 2)
        XCTAssertEqual(intervals[0]["start"], 10)
        XCTAssertEqual(intervals[0]["end"], 50)
        XCTAssertEqual(intervals[1]["start"], 70)
        XCTAssertEqual(intervals[1]["end"], 80)
    }

    func testSinglePairPreservesTheSteppedSequence() throws {
        let result = try evaluate(
            """
            const trace = {spans: [stage(0,1000),
                {id: 2, parentId: 1, stage: 'matching', name: 'match-pair', kind: 'checkpoint', status: 'completed', startMilliseconds: 100, durationMilliseconds: 100},
                call(3,110,5,2,'Load'), call(4,115,60,2,'Match'), call(5,175,20,2,'Verify'),
                {id: 6, parentId: 1, stage: 'matching', name: 'match-pair', kind: 'checkpoint', status: 'completed', startMilliseconds: 300, durationMilliseconds: 100},
                call(7,310,80,6,'Match')], statistics: []};
            const model = buildSubstepGantt(trace, buildStageOverview(trace)[0]);
            return {count: model.items.length, view: buildItemGantt(model.items[0])};
            """)
        XCTAssertEqual(result["count"] as? Int, 2)
        let view = try XCTUnwrap(result["view"] as? [String: Any])
        XCTAssertEqual(view["start"] as? Double, 100)
        XCTAssertEqual(view["end"] as? Double, 200)
        let rows = try XCTUnwrap(view["rows"] as? [[String: Any]])
        XCTAssertEqual(rows.compactMap { $0["name"] as? String }, ["Load", "Match", "Verify"])
        XCTAssertEqual(rows.compactMap { $0["activeTime"] as? Double }, [5, 60, 20])
        let intervals = rows.compactMap { $0["segments"] as? [[String: Double]] }.flatMap { $0 }
        XCTAssertEqual(intervals.compactMap { $0["start"] }, [110, 115, 175])
    }

    func testMissingIntervalsAreMarkedInsteadOfSubstitutingWorkerTotals() throws {
        let result = try evaluate(
            """
            const trace = {spans: [stage(0,100), call(2,10,20)], statistics: [stat(3,60)]};
            return buildSubstepGantt(trace, buildStageOverview(trace)[0]).rows[0];
            """)
        XCTAssertEqual(result["activeTime"] as? Double, 20)
        XCTAssertEqual(result["complete"] as? Bool, false)
        XCTAssertEqual(result["lowerBound"] as? Bool, true)
        XCTAssertEqual(result["workerTime"] as? Double, 60)
    }

    func testMeasuredCoverageSurvivesMissingTimelineDetail() throws {
        let result = try evaluate(
            """
            const aggregate = stat(3,60); aggregate.busyMilliseconds = 45;
            const trace = {spans: [stage(0,100), call(2,10,20)], statistics: [aggregate]};
            return buildSubstepGantt(trace, buildStageOverview(trace)[0]).rows[0];
            """)
        XCTAssertEqual(result["activeTime"] as? Double, 45)
        XCTAssertEqual(result["complete"] as? Bool, false)
        XCTAssertEqual(result["lowerBound"] as? Bool, false)
    }

    func testZeroAndMissingDurationsAreNotConfused() throws {
        let zero = try evaluate(
            """
            const trace = {spans: [stage(0,100), call(2,10,0)], statistics: [stat(1,0)]};
            return buildSubstepGantt(trace, buildStageOverview(trace)[0]).rows[0];
            """)
        XCTAssertEqual(zero["activeTime"] as? Double, 0)
        XCTAssertEqual(zero["complete"] as? Bool, true)
        let missing = try evaluate(
            """
            const trace = {spans: [stage(0,100)], statistics: [stat(5,700)]};
            return buildSubstepGantt(trace, buildStageOverview(trace)[0]).rows[0];
            """)
        XCTAssertTrue(missing["activeTime"] is NSNull)
        XCTAssertEqual(missing["complete"] as? Bool, false)
    }

    func testIntervalsStayInsideTheParentAndBackendsStaySeparate() throws {
        let result = try evaluate(
            """
            const first = call(2,90,20); const second = call(3,180,30); second.backend = 'metal';
            const trace = {spans: [stage(100,100), first, second], statistics: []};
            return {rows: buildSubstepGantt(trace, buildStageOverview(trace)[0]).rows};
            """)
        let rows = try XCTUnwrap(result["rows"] as? [[String: Any]])
        XCTAssertEqual(rows.count, 2)
        XCTAssertEqual(rows.compactMap { $0["activeTime"] as? Double }, [10, 20])
        let intervals = rows.compactMap { $0["segments"] as? [[String: Double]] }.flatMap { $0 }
        XCTAssertEqual(intervals.compactMap { $0["start"] }, [100, 180])
        XCTAssertEqual(intervals.compactMap { $0["end"] }, [110, 200])
    }

    private func evaluate(_ body: String) throws -> [String: Any] {
        let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
        let path = repository.appendingPathComponent("src/Echo.Telemetry/PerformanceTraceViewer.html")
        let html = try String(contentsOf: path, encoding: .utf8)
        let start = try XCTUnwrap(html.range(of: "<script id=\"overview-model\">"))
        let end = try XCTUnwrap(html.range(of: "</script>", range: start.upperBound..<html.endIndex))
        let context = try XCTUnwrap(JSContext())
        context.evaluateScript(String(html[start.upperBound..<end.lowerBound]))
        XCTAssertNil(context.exception)
        context.evaluateScript(
            """
            function stage(start, duration) { return {id:1, stage:'matching', name:'matching', kind:'stage', status:'completed', startMilliseconds:start, durationMilliseconds:duration}; }
            function call(id,start,duration,parent=1,name='operation') { return {id,parentId:parent,stage:'matching',name,kind:'operation',backend:'cpu',status:'completed',startMilliseconds:start,durationMilliseconds:duration}; }
            function stat(count,total) { return {stage:'matching',name:'operation',kind:'operation',backend:'cpu',count,totalMilliseconds:total}; }
            """)
        let value = context.evaluateScript("JSON.stringify((() => {\(body)})())")
        XCTAssertNil(context.exception)
        let json = try XCTUnwrap(value?.toString()).data(using: .utf8)
        return try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(json)) as? [String: Any])
    }
}
