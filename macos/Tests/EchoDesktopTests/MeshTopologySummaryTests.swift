import XCTest

@testable import EchoDesktop

final class MeshTopologySummaryTests: XCTestCase {
    func testClosedHullRequiresNonemptyManifoldGeometry() throws {
        let report = try JSONDecoder().decode(
            MeshArtifactReport.self,
            from: Data(
                """
                {"closeHull":true,"topology":{"mainHullFaces":4,"boundaryEdges":0,
                "nonManifoldEdges":0,"windingConflicts":0,"degenerateFaces":0,"closed":true}}
                """.utf8))
        XCTAssertTrue(try XCTUnwrap(report.topology).closed)
        XCTAssertTrue(try XCTUnwrap(report.topology).detail.contains("0 open edges"))
        let invalid = MeshTopologySummary(
            mainHullFaces: 4, boundaryEdges: 0, nonManifoldEdges: 3, windingConflicts: 0, degenerateFaces: 0)
        XCTAssertFalse(invalid.closed)
        XCTAssertTrue(invalid.detail.contains("topology defects"))
        let empty = MeshTopologySummary(
            mainHullFaces: 0, boundaryEdges: 0, nonManifoldEdges: 0, windingConflicts: 0, degenerateFaces: 0)
        XCTAssertFalse(empty.closed)
    }

    func testHistoricalMeshReportsDoNotInventClosureEvidence() throws {
        let report = try JSONDecoder().decode(MeshArtifactReport.self, from: Data("{}".utf8))
        XCTAssertNil(report.topology)
        XCTAssertNil(report.closeHull)
    }
}
