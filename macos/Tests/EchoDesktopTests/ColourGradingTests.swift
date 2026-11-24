import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class ColourGradingTests: XCTestCase {
    func testActualColourBinsCountEveryPointAndUseObservedSwatches() {
        var histogram = ColourHistogram()
        let rock = SIMD3<UInt8>(145, 108, 78)
        let similarRock = SIMD3<UInt8>(159, 120, 90)
        let vegetation = SIMD3<UInt8>(40, 95, 35)
        histogram.add(rock, count: 2000)
        histogram.add(similarRock, count: 500)
        histogram.add(vegetation, count: 250)
        let bins = histogram.scanColours.filter { $0.count > 0 }
        XCTAssertEqual(bins.count, 2)
        XCTAssertEqual(bins.reduce(0) { $0 + $1.count }, histogram.count)
        XCTAssertEqual(histogram.count, 2750)
        XCTAssertEqual(bins.first { $0.colour == rock }?.count, 2500)
        XCTAssertEqual(bins.first { $0.colour == rock }?.hex, "#916C4E")
        XCTAssertEqual(bins.first { $0.colour == vegetation }?.count, 250)
    }

    func testScalarRecipeMatchesProcessingReferenceColours() {
        var grade = ColourGrade()
        for value in 0...255 {
            let colour = SIMD3(UInt8(value), UInt8(255 - value), 37)
            XCTAssertEqual(ColourGradeTransform(grade).apply(colour), colour)
        }
        grade.exposure = 1
        XCTAssertEqual(ColourGradeTransform(grade).apply(SIMD3(128, 128, 128)), SIMD3(176, 176, 176))
        grade = ColourGrade()
        grade.saturation = 0
        XCTAssertEqual(ColourGradeTransform(grade).apply(SIMD3(220, 80, 30)), SIMD3(106, 106, 106))
        grade = ColourGrade()
        grade.temperature = 1
        XCTAssertEqual(ColourGradeTransform(grade).apply(SIMD3(128, 128, 128)), SIMD3(150, 128, 109))
        grade.exposure = .nan
        XCTAssertFalse(grade.isValid)
    }

    func testSettingsRoundTripAndOnlyTextureBecomesOutdated() throws {
        let original = try JSONDecoder().decode(ProcessingSettings.self, from: Data("{}".utf8))
        XCTAssertNil(original.colourGrade)
        var settings = original
        settings.colourGrade = ColourGrade()
        XCTAssertNil(ProcessingImpact().firstChangedStage(from: original, to: settings))
        settings.colourGrade?.exposure = 1.5
        settings.colourGrade?.vibrance = 0.3
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(restored.colourGrade, settings.colourGrade)
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: restored), .texture)
    }

    func testFullCloudHistogramRespectsCropAndSupportWithoutDisplaySampling() throws {
        let file = try fixture(count: 1001)
        defer { try? FileManager.default.removeItem(at: file) }
        let reader = CloudColourHistogramReader()
        let full = try reader.read(file: file, expectedCount: 1001, regions: [], minimumSupport: 0)
        let histogram = try full.histogram(grade: ColourGrade())
        XCTAssertEqual(full.scanned, 1001)
        XCTAssertEqual(histogram.count, 1001)
        XCTAssertEqual(histogram.red[128], 1001)
        let preview = try DenseCloudPreviewReader().read(file: file, expectedCount: 1001, limit: 10, minimumSupport: 0)
        XCTAssertEqual(preview.count, 10)
        var crop = ExclusionRegion()
        crop.centreX = 500
        crop.width = 200
        crop.height = 2
        crop.depth = 2
        let selected = try reader.read(file: file, expectedCount: 1001, regions: [crop], minimumSupport: 0.5)
        XCTAssertEqual(selected.included, 101)
        var grade = ColourGrade()
        grade.exposure = 1
        let graded = try selected.histogram(grade: grade)
        XCTAssertEqual(graded.count, 101)
        XCTAssertEqual(graded.red[176], 101)
        XCTAssertEqual(graded.green[176], 101)
        XCTAssertEqual(graded.blue[176], 101)
        XCTAssertEqual(try selected.histogram(grade: ColourGrade()).red[128], 101)
        XCTAssertThrowsError(try reader.read(file: file, expectedCount: 1000, regions: [], minimumSupport: 0))
        var bytes = try Data(contentsOf: file)
        bytes.removeLast()
        try bytes.write(to: file)
        XCTAssertThrowsError(try reader.read(file: file, expectedCount: 1001, regions: [], minimumSupport: 0))
    }

    func testHistogramCancellationIsObserved() async throws {
        let file = try fixture(count: 1001)
        defer { try? FileManager.default.removeItem(at: file) }
        let task = Task.detached {
            withUnsafeCurrentTask { $0?.cancel() }
            return try CloudColourHistogramReader().read(
                file: file, expectedCount: 1001, regions: [], minimumSupport: 0)
        }
        do {
            _ = try await task.value
            XCTFail("Cancelled histogram should not complete")
        } catch is CancellationError {}
    }

    @MainActor
    func testGPUPreviewUsesSameExposureAndResetPreservesPackedBuffer() throws {
        guard let device = MTLCreateSystemDefaultDevice() else { throw XCTSkip("Metal unavailable") }
        let file = try fixture(count: 1)
        defer { try? FileManager.default.removeItem(at: file) }
        let preview = try DenseCloudPreviewReader().read(file: file, expectedCount: 1, limit: 1, minimumSupport: 0)
        let buffer = try XCTUnwrap(preview.geometry.sources(for: .color).first?.data)
        preview.geometry.elements.first?.pointSize = 64
        preview.geometry.elements.first?.minimumPointScreenSpaceRadius = 32
        preview.geometry.elements.first?.maximumPointScreenSpaceRadius = 32
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: file.deletingLastPathComponent(),
            cloudGeometry: preview.geometry)
        let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        camera.position = SCNVector3(0, 0, 5)
        camera.eulerAngles = SCNVector3Zero
        camera.camera?.usesOrthographicProjection = true
        camera.camera?.orthographicScale = 1
        let cameraTransform = camera.transform
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        let original = try centreColour(renderer)
        XCTAssertEqual(original.x, 128, accuracy: 2)
        var grade = ColourGrade()
        grade.exposure = 1
        ColourGradePreview.apply(grade, to: scene)
        let graded = try centreColour(renderer)
        XCTAssertEqual(graded.x, 176, accuracy: 3)
        XCTAssertEqual(graded.y, 176, accuracy: 3)
        XCTAssertEqual(graded.z, 176, accuracy: 3)
        XCTAssertGreaterThan(graded.x, original.x + 20)
        grade.temperature = -0.3
        grade.tint = 0.2
        grade.blackPoint = 0.05
        grade.whitePoint = 0.95
        grade.contrast = 1.1
        grade.shadows = 0.4
        grade.highlights = -0.2
        grade.saturation = 0.8
        grade.vibrance = 0.3
        ColourGradePreview.apply(grade, to: scene)
        let complex = try centreColour(renderer)
        let expected = ColourGradeTransform(grade).apply(SIMD3(128, 128, 128))
        XCTAssertEqual(complex.x, Double(expected.x), accuracy: 2)
        XCTAssertEqual(complex.y, Double(expected.y), accuracy: 2)
        XCTAssertEqual(complex.z, Double(expected.z), accuracy: 2)
        ColourGradePreview.apply(nil, to: scene)
        XCTAssertTrue(SCNMatrix4EqualToMatrix4(camera.transform, cameraTransform), "Grading must preserve the view")
        let reset = try centreColour(renderer)
        XCTAssertEqual(reset.x, original.x, accuracy: 1)
        XCTAssertEqual(preview.geometry.sources(for: .color).first?.data, buffer)
    }

    @MainActor
    func testReplacingAndClosingHistogramDiscardsStaleWork() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(
            "echo-grade-model-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        try FileManager.default.moveItem(at: fixture(count: 1001), to: folder.appendingPathComponent("cloud.ply"))
        try PackedFixture.emptyCloud(folder.appendingPathComponent("cloud.bin"), count: 1001)
        let model = ColourGradingModel()
        model.scan(folder: folder, regions: [], minimumSupport: 0)
        model.cancel()
        model.grade.exposure = 1
        model.scan(folder: folder, regions: [], minimumSupport: 0.5)
        let deadline = Date().addingTimeInterval(15)
        while model.histogram == nil && model.error == nil && Date() < deadline {
            try await Task.sleep(for: .milliseconds(20))
        }
        XCTAssertNil(model.error)
        XCTAssertEqual(model.histogram?.count, 501)
        XCTAssertEqual(model.histogram?.red[176], 501)
        model.grade.exposure = -1
        model.cancel()
        try await Task.sleep(for: .milliseconds(180))
        XCTAssertNil(model.histogram)
        XCTAssertFalse(model.scanning)
        XCTAssertFalse(model.updating)
        XCTAssertEqual(model.grade.exposure, -1, "Closing the panel retains the user's recipe")
    }

    @MainActor
    private func centreColour(_ renderer: SCNRenderer) throws -> SIMD3<Double> {
        let image = renderer.snapshot(atTime: 0, with: CGSize(width: 128, height: 128), antialiasingMode: .none)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
        let colour = try XCTUnwrap(bitmap.colorAt(x: 64, y: 64))
        return SIMD3(colour.redComponent * 255, colour.greenComponent * 255, colour.blueComponent * 255)
    }

    private func fixture(count: Int) throws -> URL {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("echo-grade-\(UUID().uuidString).ply")
        var data = Data(
            "ply\nformat binary_little_endian 1.0\nelement vertex \(count)\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nproperty int view\nproperty float confidence\nend_header\n"
                .utf8)
        for index in 0..<count {
            for value in [Float(index), 0, 0] {
                var bits = value.bitPattern.littleEndian
                withUnsafeBytes(of: &bits) { data.append(contentsOf: $0) }
            }
            data.append(contentsOf: [128, 128, 128])
            var view = Int32(0)
            withUnsafeBytes(of: &view) { data.append(contentsOf: $0) }
            var support = Float(index.isMultiple(of: 2) ? 1 : 0.3).bitPattern.littleEndian
            withUnsafeBytes(of: &support) { data.append(contentsOf: $0) }
        }
        try data.write(to: file)
        return file
    }
}
