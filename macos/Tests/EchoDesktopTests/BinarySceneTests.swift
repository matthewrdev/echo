import Foundation
import XCTest

@testable import EchoDesktop

final class BinarySceneTests: XCTestCase {
    func testPointPrecisionObservationsAndMetadataWithoutPointBuffer() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let file = root.appendingPathComponent("scene.json")
        try BinaryArtifactFixture.write(
            file,
            value: [
                "frame": "local metres", "origin": ["latitude": -35, "longitude": 150],
                "cameras": [], "unplaced": ["missing"], "warnings": ["warning"],
                "points": [
                    [
                        "position": [Double.pi, -0.0, Double.leastNonzeroMagnitude],
                        "pixelA": [4031.5, 2267.25], "pixelB": [], "error": Double.greatestFiniteMagnitude,
                        "imageA": "rock", "imageB": "photo-é", "colour": "AID/",
                    ]
                ],
            ])
        let reader = ArtifactJSONReader()
        let scene: ReconstructionScene = try reader.read(file)
        let point = try XCTUnwrap(scene.points.first)
        XCTAssertEqual(
            point.position.map(\.bitPattern), [Double.pi, -0.0, Double.leastNonzeroMagnitude].map(\.bitPattern))
        XCTAssertEqual(point.pixelA, [4031.5, 2267.25])
        XCTAssertEqual(point.pixelB, [])
        XCTAssertEqual(point.imageA, "rock")
        XCTAssertEqual(point.imageB, "photo-é")
        XCTAssertEqual(point.colour, Data([0, 128, 255]))
        XCTAssertEqual(point.error, Double.greatestFiniteMagnitude)
        XCTAssertEqual(scene.unplaced, ["missing"])
        XCTAssertEqual(scene.warnings, ["warning"])
        let header = try JSONDecoder().decode(BulkArtifactHeader.self, from: Data(contentsOf: file))
        try FileManager.default.removeItem(at: root.appendingPathComponent(try XCTUnwrap(header.buffers["points"])))
        let metadata: ReconstructionMetadata = try ArtifactJSONReader().read(file)
        XCTAssertEqual(metadata.origin?.latitude, -35)
        XCTAssertTrue(metadata.isMetric)
        XCTAssertThrowsError(try ArtifactJSONReader().read(file) as ReconstructionScene)
    }
}
