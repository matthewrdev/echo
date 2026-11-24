import AppKit
import EchoRendering
import Metal
import SceneKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class SavedColourGradingTests: XCTestCase {
    @MainActor
    func testSavedGradingEditorLayout() async throws {
        try WindowedTests.requireOptIn()

        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_CLOUD_PROJECT"], let id = environment["ECHO_TEST_CLOUD_RUN"],
            let cli = environment["ECHO_TEST_CLI"], let output = environment["ECHO_TEST_RENDER_DIR"]
        else { throw XCTSkip("Saved grading workspace not configured") }
        let domain = "echo-grading-test-\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: domain))
        defer { defaults.removePersistentDomain(forName: domain) }
        let workspace = WorkspaceViewModel(
            client: CLIProjectClient(cli: ProcessCLIClient(executable: URL(fileURLWithPath: cli))), project: path,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults), clipboard: SystemTextClipboard())
        workspace.selectedRun = id
        await workspace.reload()
        XCTAssertNotNil(workspace.displayedRun, workspace.status)
        let host = NSHostingView(
            rootView: ColourGradingEditor(workspace: workspace, model: workspace.colourGrading, minimumSupport: 0)
                .padding(16).frame(width: 350, height: 1000).environment(\.colorScheme, .light).background(Color.white))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 350, height: 1000), styleMask: [.titled], backing: .buffered,
            defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer {
            window.orderOut(nil)
            workspace.colourGrading.cancel()
        }
        host.layoutSubtreeIfNeeded()
        let deadline = Date().addingTimeInterval(30)
        while workspace.colourGrading.histogram == nil && workspace.colourGrading.error == nil && Date() < deadline {
            try await Task.sleep(for: .milliseconds(50))
        }
        XCTAssertNil(workspace.colourGrading.error)
        XCTAssertGreaterThan(workspace.colourGrading.histogram?.count ?? 0, 4_000_000)
        host.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
        host.cacheDisplay(in: host.bounds, to: bitmap)
        try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
            to: URL(fileURLWithPath: output).appendingPathComponent("grading-editor.png"))
        let sliderFrames = gradingSliderFrames(in: host)
        XCTAssertEqual(sliderFrames.count, 10)
        workspace.colourGrading.grade.saturation = 1.2
        XCTAssertTrue(workspace.colourGrading.updating)
        try await Task.sleep(for: .milliseconds(30))
        host.layoutSubtreeIfNeeded()
        XCTAssertEqual(gradingSliderFrames(in: host), sliderFrames, "Updating must not move the sliders")
        let updateDeadline = Date().addingTimeInterval(10)
        while workspace.colourGrading.updating && Date() < updateDeadline {
            try await Task.sleep(for: .milliseconds(20))
        }
        host.layoutSubtreeIfNeeded()
        XCTAssertEqual(gradingSliderFrames(in: host), sliderFrames, "The new histogram must preserve slider positions")
        let originalRegions = workspace.inspection?.project.settings.exclusionRegions
        let originalGrade = workspace.colourGrading.grade
        let originalCount = workspace.colourGrading.histogram?.count ?? 0
        let cloud = URL(fileURLWithPath: path).appendingPathComponent(
            try XCTUnwrap(workspace.displayedRun?.artifacts["cloud"]))
        let report = try PackedPreviewReader().cloud(Data(contentsOf: cloud.appendingPathComponent("cloud.bin")))
        let point = try XCTUnwrap(report.preview.first).position
        var selection = ExclusionRegion()
        selection.centreX = point[0]
        selection.centreY = point[1]
        selection.centreZ = point[2]
        selection.width = 20
        selection.height = 20
        selection.depth = 20
        workspace.colourGrading.selectingRegion = true
        workspace.colourGrading.select(selection)
        let selectedDeadline = Date().addingTimeInterval(30)
        while workspace.colourGrading.histogram?.count == originalCount || workspace.colourGrading.scanning
            || workspace.colourGrading.updating
        {
            if Date() >= selectedDeadline { break }
            try await Task.sleep(for: .milliseconds(50))
        }
        XCTAssertNil(workspace.colourGrading.error)
        XCTAssertGreaterThan(workspace.colourGrading.histogram?.count ?? 0, 0)
        XCTAssertLessThan(workspace.colourGrading.histogram?.count ?? originalCount, originalCount)
        XCTAssertEqual(workspace.inspection?.project.settings.exclusionRegions, originalRegions)
        XCTAssertEqual(workspace.colourGrading.grade, originalGrade)
        XCTAssertFalse(workspace.colourGrading.selectingRegion)
        host.layoutSubtreeIfNeeded()
        let selectedBitmap = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
        host.cacheDisplay(in: host.bounds, to: selectedBitmap)
        try XCTUnwrap(selectedBitmap.representation(using: .png, properties: [:])).write(
            to: URL(fileURLWithPath: output).appendingPathComponent("grading-selected-region.png"))
    }

    @MainActor
    func testSavedCloudHistogramAndRenderedGrade() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_CLOUD_PROJECT"], let id = environment["ECHO_TEST_CLOUD_RUN"],
            let output = environment["ECHO_TEST_RENDER_DIR"]
        else { throw XCTSkip("Saved cloud not configured") }
        let root = URL(fileURLWithPath: path)
        let run = try await SavedRunFixture.read(root, id: id)
        let cloud = root.appendingPathComponent(try XCTUnwrap(run.artifacts["cloud"]))
        let report = try PackedPreviewReader().cloud(Data(contentsOf: cloud.appendingPathComponent("cloud.bin")))
        let start = Date()
        let frequencies = try await Task.detached(priority: .utility) {
            try CloudColourHistogramReader().read(
                file: cloud.appendingPathComponent("cloud.ply"), expectedCount: report.retainedSamples, regions: [],
                minimumSupport: 0)
        }.value
        let histogram = try await Task.detached(priority: .utility) { try frequencies.histogram(grade: ColourGrade()) }
            .value
        XCTAssertEqual(histogram.count, UInt64(report.retainedSamples))
        print("Full saved histogram: \(histogram.count) points in \(Date().timeIntervalSince(start)) seconds")
        var grade = ColourGrade()
        grade.exposure = 0.3
        grade.contrast = 1.12
        grade.shadows = 0.3
        grade.temperature = 0.15
        grade.vibrance = 0.25
        let recipe = grade
        let regradeStart = Date()
        let gradedHistogram = try await Task.detached(priority: .utility) { try frequencies.histogram(grade: recipe) }
            .value
        XCTAssertEqual(gradedHistogram.count, histogram.count)
        print("Full histogram regrade: \(Date().timeIntervalSince(regradeStart)) seconds")
        let preview = try await Task.detached(priority: .utility) {
            try DenseCloudPreviewReader().read(
                file: cloud.appendingPathComponent("cloud.ply"), expectedCount: report.retainedSamples,
                limit: 4_000_000, minimumSupport: 0)
        }.value
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: root, cloudGeometry: preview.geometry)
        guard let device = MTLCreateSystemDefaultDevice() else { throw XCTSkip("Metal unavailable") }
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        renderer.pointOfView = camera
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let bounds = try representativeBounds(artifact)
        let alignment = root.appendingPathComponent(try XCTUnwrap(run.artifacts["alignment"]))
        let reconstruction: ReconstructionMetadata = try ArtifactJSONReader().read(
            alignment.appendingPathComponent("scene.json"))
        let view = PhotographedView(bounds: bounds, cameras: reconstruction.cameras)
        bounds.fit(camera: camera, size: CGSize(width: 1200, height: 800), offset: view.offset, up: view.up)
        scene.background.contents = NSColor.white
        scene.rootNode.childNode(withName: "ground-grid", recursively: false)?.isHidden = true
        let folder = URL(fileURLWithPath: output)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try save(
            renderer.snapshot(atTime: 0, with: CGSize(width: 1200, height: 800), antialiasingMode: .multisampling4X),
            to: folder.appendingPathComponent("cloud-original.png"))
        ColourGradePreview.apply(grade, to: scene)
        try save(
            renderer.snapshot(atTime: 0, with: CGSize(width: 1200, height: 800), antialiasingMode: .multisampling4X),
            to: folder.appendingPathComponent("cloud-graded.png"))
        let histograms = NSHostingView(
            rootView: HStack(spacing: 24) {
                VStack {
                    Text("Original · \(histogram.count.formatted()) points")
                    ColourHistogramView(histogram: histogram)
                }
                VStack {
                    Text("Graded · \(gradedHistogram.count.formatted()) points")
                    ColourHistogramView(histogram: gradedHistogram)
                }
            }.padding(20).background(Color.white).environment(\.colorScheme, .light))
        histograms.frame = CGRect(x: 0, y: 0, width: 1000, height: 300)
        histograms.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(histograms.bitmapImageRepForCachingDisplay(in: histograms.bounds))
        histograms.cacheDisplay(in: histograms.bounds, to: bitmap)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
            to: folder.appendingPathComponent("full-cloud-histograms.png"))
    }

    private func save(_ image: NSImage, to file: URL) throws {
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: file)
    }

    @MainActor
    private func gradingSliderFrames(in view: NSView) -> [NSRect] {
        if view is NSSlider { return [view.convert(view.bounds, to: nil)] }
        return view.subviews.flatMap { gradingSliderFrames(in: $0) }
    }

    @MainActor
    private func representativeBounds(_ artifact: SCNNode) throws -> SceneBounds {
        let source = try XCTUnwrap(CropPointSource(node: artifact))
        let points = source.data.withUnsafeBytes { bytes in
            stride(from: 0, to: source.count, by: max(1, source.count / 4096)).map { source.position($0, bytes: bytes) }
        }
        let axes = (0..<3).map { axis in points.map { $0[axis] }.sorted() }
        let low = axes.map { $0[$0.count / 50] }
        let high = axes.map { $0[$0.count - 1 - $0.count / 50] }
        return SceneBounds(positions: [low, high])
    }
}
