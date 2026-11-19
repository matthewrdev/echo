import Foundation
import XCTest

@testable import EchoDesktop

final class LivePreviewRefreshTests: XCTestCase {
    @MainActor
    func testFrequentSnapshotsDoNotStarveASlowerViewerAndLatestWins() async throws {
        let reader = SlowArtifactPreviewReader()
        let model = ArtifactViewModel(reader: reader)
        func run(_ revision: Int) -> RunData {
            RunData(
                id: "live", status: "running", target: "alignment",
                snapshot: ProjectData(
                    id: "project", name: "Project", revision: 1, images: [], settings: ProcessingSettings(), hints: []),
                artifacts: ["alignmentPreview": String(revision)], error: nil, progress: nil)
        }
        let initial = Task { await model.configure(root: "/tmp", run: run(0), stage: .alignment) }
        for _ in 0..<100 {
            if await !reader.requests.isEmpty { break }
            try await Task.sleep(for: .milliseconds(5))
        }
        XCTAssertTrue(model.loading)
        for revision in 1...5 { await model.configure(root: "/tmp", run: run(revision), stage: .alignment) }
        await initial.value
        for _ in 0..<100 {
            if model.presentation?.detail == "5", !model.loading { break }
            try await Task.sleep(for: .milliseconds(10))
        }
        let requests = await reader.requests
        let cancellations = await reader.cancellations
        XCTAssertEqual(requests, ["0", "5"])
        XCTAssertEqual(cancellations, 0)
        XCTAssertEqual(model.presentation?.detail, "5")
        XCTAssertNil(model.error)
    }
}
