import Foundation
import XCTest

@testable import EchoDesktop

final class AlignmentPreviewTests: XCTestCase {
    func testAtomicSnapshotReadsObservationsAndRejectsTruncation() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let file = root.appendingPathComponent("preview.bin")
        let data = try snapshot(x: 12)
        try data.write(to: file, options: .atomic)
        let reader = ArtifactJSONReader()
        let original: AlignmentPreview = try reader.read(file)
        XCTAssertEqual(original.totalPoints, 1)
        XCTAssertEqual(original.scene.points[0].position, [12, 2, 3])
        XCTAssertEqual(original.scene.points[0].imageA, "first")
        XCTAssertEqual(original.scene.points[0].pixelB, [30, 40])
        XCTAssertEqual(original.scene.points[0].colour, Data([83, 210, 185]))
        XCTAssertThrowsError(try AlignmentPreviewReader().read(data.dropLast()))
        XCTAssertThrowsError(try AlignmentPreviewReader().read(Data(repeating: 255, count: 24)))
        try snapshot(x: 13).write(to: file, options: .atomic)
        let updated: AlignmentPreview = try reader.read(file)
        XCTAssertEqual(updated.scene.points[0].position[0], 13)
        XCTAssertEqual(original.scene.points[0].position[0], 12)
    }

    func testProcessorSnapshotSupportsAutoAllAndQualityFilters() async throws {
        guard let path = ProcessInfo.processInfo.environment["ECHO_TEST_ALIGNMENT_PREVIEW_OUTPUT"] else {
            throw XCTSkip("Set the C# preview fixture folder to verify the shared binary snapshot")
        }
        let start = Date()
        let preview: AlignmentPreview = try ArtifactJSONReader().read(
            URL(fileURLWithPath: path).appendingPathComponent("preview.bin"))
        let elapsed = Date().timeIntervalSince(start)
        XCTAssertEqual(preview.totalPoints, 1_100_001)
        XCTAssertEqual(preview.scene.points.count, 1_100_001)
        XCTAssertEqual(preview.scene.points.last?.position, [0, 1100, 10])
        XCTAssertEqual(
            SparsePointPreview(points: preview.scene.points, options: SparseDisplayOptions()).points.count, 1_000_000)
        XCTAssertEqual(
            SparsePointPreview(points: preview.scene.points, options: SparseDisplayOptions(detail: .percentage(100)))
                .points.count,
            1_100_001)
        XCTAssertEqual(
            SparsePointPreview(points: preview.scene.points, options: SparseDisplayOptions(maximumReprojectionError: 1))
                .eligibleCount,
            550_001)
        print("Sparse preview: \(preview.totalPoints) points decoded in \(elapsed) seconds")
        let root = URL(fileURLWithPath: path)
        let run = RunData(
            id: "preview-review", status: "running", target: "alignment",
            snapshot: ProjectData(
                id: "preview-review", name: "Preview", revision: 1, images: [],
                settings: ProcessingSettings(), hints: []),
            artifacts: ["alignmentPreview": "."], error: nil, progress: nil)
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let loadStart = Date()
        let presentation = try await reader.load(
            root: root, run: run, stage: .alignment, unit: nil, mode: "", slice: 0,
            sparseOptions: SparseDisplayOptions(detail: .percentage(100)))
        XCTAssertTrue(presentation.detail.contains("displaying 1,100,001 of 1,100,001"))
        XCTAssertTrue(presentation.detail.contains("provisional alignment"))
        XCTAssertEqual(presentation.sparseInspection?.tracks.count, 1_100_001)
        XCTAssertEqual(
            presentation.scene?.rootNode.childNode(withName: "artifact", recursively: false)?.geometry?.sources(
                for: .vertex
            ).first?.vectorCount, 1_100_001)
        print("Sparse preview: full All viewer loaded in \(Date().timeIntervalSince(loadStart)) seconds")
    }

    private func snapshot(x: Double) throws -> Data {
        let header: [String: Any] = [
            "scene": ["frame": "local", "cameras": [], "points": [], "unplaced": [], "warnings": []],
            "totalPoints": 1, "images": ["first", "second"],
        ]
        let json = try JSONSerialization.data(withJSONObject: header)
        var data = Data()
        func integer(_ value: Int32) {
            var encoded = value.littleEndian
            withUnsafeBytes(of: &encoded) { data.append(contentsOf: $0) }
        }
        integer(0x3150_5345)
        integer(Int32(json.count))
        data.append(json)
        for value: Int32 in [0x3150_4345, 1, 76, 1] { integer(value) }
        for value in [x, 2, 3, 10, 20, 30, 40, 0.5] {
            var encoded = value.bitPattern.littleEndian
            withUnsafeBytes(of: &encoded) { data.append(contentsOf: $0) }
        }
        integer(0)
        integer(1)
        data.append(contentsOf: [15, 83, 210, 185])
        return data
    }
}
