import Foundation

@testable import EchoDesktop

enum PerformanceTraceFixture {
    static func document(
        id: String, run: String, second: Int, duration: Double, reused: Int = 0,
        stageStatus: String = "completed", status: String = "completed"
    ) throws -> PerformanceTraceDocument {
        let date = String(format: "2026-11-16T20:00:%02d.000+00:00", second)
        let object: [String: Any] = [
            "schemaVersion": 1,
            "summary": [
                "id": id, "runId": run, "target": "features", "startedAt": date, "updatedAt": date,
                "status": status, "durationMilliseconds": duration + 100, "cpuMilliseconds": duration * 2,
                "droppedSpans": 0,
            ],
            "spans": [
                [
                    "id": 1, "name": "features", "stage": "features", "kind": "stage", "backend": "mixed",
                    "status": stageStatus, "threadId": 1, "startMilliseconds": 100,
                    "durationMilliseconds": duration, "unit": "items", "bytes": 0,
                ]
            ],
            "statistics": [
                [
                    "stage": "features", "name": "feature-image", "kind": "checkpoint", "backend": "mixed",
                    "count": 10, "reusedCount": reused, "failedCount": 0, "totalMilliseconds": duration,
                    "maximumMilliseconds": duration / 10, "items": 0, "bytes": 0,
                ],
                [
                    "stage": "features", "name": "features", "kind": "compute", "backend": "metal",
                    "count": 1, "reusedCount": 0, "failedCount": 0, "totalMilliseconds": duration,
                    "maximumMilliseconds": duration, "gpuMilliseconds": reused == 10 ? 0 : 4500,
                    "items": 0, "bytes": 0,
                ],
            ],
        ]
        return try JSONDecoder().decode(
            PerformanceTraceDocument.self, from: JSONSerialization.data(withJSONObject: object))
    }
}
