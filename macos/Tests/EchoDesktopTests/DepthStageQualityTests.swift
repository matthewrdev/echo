import XCTest

@testable import EchoDesktop

final class DepthStageQualityTests: XCTestCase {
    func testOverallCoverageWeightsPixelsRatherThanImagePercentages() throws {
        let small = quality(Array(repeating: 10, count: 10))
        let large = quality(Array(repeating: 0, count: 90))
        let summary = DepthStageQualitySummary(maps: [small, large], isPreview: false)
        XCTAssertEqual(try XCTUnwrap(summary.coverage), 0.1, accuracy: 0.0001)
        XCTAssertEqual(summary.grade, .limited)
        XCTAssertEqual(summary.mapCount, 2)
        XCTAssertEqual(summary.accepted, 10)
        XCTAssertEqual(summary.total, 100)
        XCTAssertEqual(summary.detail, "Overall depth coverage")
    }

    func testMasksAndMissingMasksUseTheSameBasisAsIndividualDepthGrades() throws {
        let sky = SkyProbabilityMap(imageId: "image", width: 2, height: 1, sky: [255, 255])
        let masked = quality([0, 0], sky: sky)
        let unmasked = quality([10, 10, 10, 0])
        let summary = DepthStageQualitySummary(maps: [masked, unmasked], isPreview: true)
        XCTAssertEqual(try XCTUnwrap(summary.coverage), 0.75)
        XCTAssertEqual(summary.grade, .strong)
        XCTAssertEqual(summary.maskedMapCount, 1)
        XCTAssertEqual(summary.excludedSkyPixels, 2)
        XCTAssertTrue(summary.detail.contains("Live coverage"))
        XCTAssertTrue(summary.help.contains("others use whole-image coverage"))
        let allSky = DepthStageQualitySummary(maps: [masked], isPreview: false)
        XCTAssertNil(allSky.coverage)
        XCTAssertNil(allSky.grade)
        XCTAssertEqual(allSky.detail, "No surface pixels to grade")
        XCTAssertNil(DepthStageQualitySummary(maps: [], isPreview: true).label)
        XCTAssertEqual(DepthStageQualitySummary(maps: [quality([0, 0])], isPreview: false).grade, .empty)
    }

    func testIncrementalIndexesFinalResultAndChangedMapsAreRecomputed() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try writeMap(root, path: "artifacts/a", values: [10, 0])
        try writeMap(root, path: "artifacts/b", values: [10, 10])
        try PackedFixture.imageIndex(root.appendingPathComponent("preview-1/units.bin"), units: ["a": "artifacts/a"])
        try PackedFixture.imageIndex(
            root.appendingPathComponent("preview-2/units.bin"), units: ["a": "artifacts/a", "b": "artifacts/b"])
        try writeJSON(root.appendingPathComponent("final/units.json"), value: ["artifacts/a", "artifacts/b"])
        let reader = DepthStageQualityReader()
        let first = try await reader.load(request(root, artifacts: ["depthPreview": "preview-1"]))
        XCTAssertEqual(first.mapCount, 1)
        XCTAssertEqual(first.coverage, 0.5)
        let second = try await reader.load(request(root, artifacts: ["depthPreview": "preview-2"]))
        XCTAssertEqual(second.mapCount, 2)
        XCTAssertEqual(second.coverage, 0.75)
        let finalRequest = try request(root, artifacts: ["depth": "final", "depthPreview": "preview-1"])
        let final = try await reader.load(finalRequest)
        XCTAssertFalse(final.isPreview)
        XCTAssertEqual(final.coverage, 0.75)
        try writeMap(root, path: "artifacts/b", values: [0, 0, 0, 0])
        let changed = try await reader.load(finalRequest)
        XCTAssertEqual(try XCTUnwrap(changed.coverage), 1.0 / 6.0, accuracy: 0.0001)
        try FileManager.default.removeItem(at: root.appendingPathComponent("artifacts/a/depth.json"))
        do {
            _ = try await reader.load(finalRequest)
            XCTFail("A missing map must not silently inflate the overall grade")
        } catch {}
    }

    func testNewSkyMasksInvalidateCachedCoverage() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try writeMap(root, path: "a", values: [0, 0, 10, 10])
        try writeJSON(root.appendingPathComponent("final/units.json"), value: ["a"])
        let reader = DepthStageQualityReader()
        let first = try await reader.load(request(root, artifacts: ["depth": "final"]))
        XCTAssertEqual(first.coverage, 0.5)
        try writeJSON(root.appendingPathComponent("semantics/units.json"), value: ["a": "sky-a"])
        try BinaryArtifactFixture.write(
            root.appendingPathComponent("sky-a/semantics.json"),
            value: ["imageId": "a", "width": 4, "height": 1, "sky": [255, 255, 0, 0]])
        let maskedRequest = try request(root, artifacts: ["depth": "final", "semantics": "semantics"])
        let masked = try await reader.load(maskedRequest)
        XCTAssertEqual(try XCTUnwrap(masked.coverage), 2.0 / 3.0, accuracy: 0.0001)
        XCTAssertEqual(masked.maskedMapCount, 1)
        try BinaryArtifactFixture.write(
            root.appendingPathComponent("sky-a/semantics.json"),
            value: ["imageId": "different-image", "width": 4, "height": 1, "sky": [255, 255, 255, 255]])
        let mismatched = try await reader.load(maskedRequest)
        XCTAssertEqual(mismatched.coverage, 0.5)
        XCTAssertEqual(mismatched.maskedMapCount, 0)
    }

    @MainActor
    func testChangingRunOrRemovingDepthClearsThePreviousGrade() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try writeMap(root, path: "a", values: [10, 10])
        try writeJSON(root.appendingPathComponent("final/units.json"), value: ["a"])
        let model = DepthStageQualityViewModel()
        await model.load(try request(root, artifacts: ["depth": "final"]))
        XCTAssertEqual(model.summary?.grade, .dense)
        await model.load(try request(root, artifacts: ["depth": "missing"]))
        XCTAssertNil(model.summary)
        XCTAssertNotNil(model.error)
        await model.load(nil)
        XCTAssertNil(model.summary)
        XCTAssertNil(model.error)
        XCTAssertFalse(model.loading)
    }

    private func quality(_ values: [Float], sky: SkyProbabilityMap? = nil) -> DepthQualitySummary {
        DepthQualitySummary(
            map: DepthMap(
                imageId: "image", width: values.count, height: 1, depth: values,
                confidence: Array(repeating: 0.99, count: values.count)), sky: sky)
    }

    private func request(_ root: URL, artifacts: [String: String]) throws -> DepthStageQualityRequest {
        try XCTUnwrap(
            DepthStageQualityRequest(
                root: root.path,
                run: RunData(
                    id: "run", status: "running", target: "depth",
                    snapshot: ProjectData(
                        id: "project", name: "Test", revision: 1, images: [], settings: ProcessingSettings(), hints: []),
                    artifacts: artifacts, error: nil, progress: nil)))
    }

    private func writeMap(_ root: URL, path: String, values: [Float]) throws {
        try BinaryArtifactFixture.write(
            root.appendingPathComponent(path).appendingPathComponent("depth.json"),
            value: [
                "imageId": path, "width": values.count, "height": 1, "depth": values,
                "confidence": Array(repeating: 0.99, count: values.count),
            ])
    }

    private func writeJSON(_ file: URL, value: Any) throws {
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONSerialization.data(withJSONObject: value).write(to: file, options: .atomic)
    }
}
