import AppKit
import EchoRendering
import Metal
import SceneKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class TexturePriorityTests: XCTestCase {
    func testSettingsRoundTripAndOnlyTextureBecomesOutdated() throws {
        let original = try JSONDecoder().decode(ProcessingSettings.self, from: Data("{}".utf8))
        XCTAssertNil(original.texturePriority)
        var settings = original
        settings.texturePriority = painting()
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(restored.texturePriority, settings.texturePriority)
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: restored), .texture)
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: restored, to: original), .texture)
        settings.texturePriority?.strokes[0].samples[0].centre[0] = 2
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: restored, to: settings), .texture)
    }

    func testPreviewMatchesProcessingFalloffAndDoesNotPaintBehindTheSurface() {
        var strokes = painting().strokes
        var field = TexturePriorityField(strokes: strokes)
        var influences: [Int: Double] = [:]
        XCTAssertEqual(field.logWeight(at: .zero, influences: &influences), 2)
        XCTAssertEqual(field.logWeight(at: SIMD3(1.5, 0, 0), influences: &influences), 1, accuracy: 1e-10)
        XCTAssertEqual(field.logWeight(at: SIMD3(0, 0, 1), influences: &influences), 0)
        strokes[0].samples.append(strokes[0].samples[0])
        field = TexturePriorityField(strokes: strokes)
        XCTAssertEqual(field.logWeight(at: SIMD3(1.5, 0, 0), influences: &influences), 1, accuracy: 1e-10)
        strokes.append(TexturePriorityStroke(weight: 1, samples: [strokes[0].samples[0]]))
        field = TexturePriorityField(strokes: strokes)
        XCTAssertEqual(field.logWeight(at: .zero, influences: &influences), 0)
    }

    @MainActor
    func testCropRunKeepsPaintingDraftAndOnlyRebuildsFromFusion() throws {
        var settings = ProcessingSettings()
        settings.texturePriority = painting()
        let model = TexturePriorityEditorModel()
        model.followRun(nil, alignment: "")
        model.followRun(settings.texturePriority, alignment: "alignment-a")
        XCTAssertEqual(model.painting, settings.texturePriority)
        XCTAssertFalse(model.incompatible)
        model.append(painting().strokes[0])
        let draft = model.painting
        var cropped = settings
        cropped.exclusionRegions = [ExclusionRegion()]
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: settings, to: cropped), .fusion)
        XCTAssertEqual(cropped.texturePriority, settings.texturePriority)
        model.followRun(cropped.texturePriority, alignment: "alignment-a")
        XCTAssertEqual(model.painting, draft)
        XCTAssertFalse(model.incompatible)
        XCTAssertEqual(model.undoHistory.count, 1)
        model.followRun(nil, alignment: "")
        model.followRun(cropped.texturePriority, alignment: "alignment-a")
        XCTAssertEqual(model.painting, draft, "Partial runs must not erase unsaved strokes")
        XCTAssertFalse(model.incompatible)
        model.followRun(cropped.texturePriority, alignment: "alignment-b")
        XCTAssertTrue(model.incompatible)
        XCTAssertEqual(model.painting, draft, "A changed alignment disables painting without discarding it")
    }

    @MainActor
    func testWorkspaceKeepsPaintingAcrossInspectionRefreshAndResetsForAnotherProject() throws {
        let domain = "echo-paint-draft-\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: domain))
        defer { defaults.removePersistentDomain(forName: domain) }
        let workspace = WorkspaceViewModel(
            client: CLIProjectClient(cli: TestProjectManagementCLIClient()), project: "/tmp/paint.echo",
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults), clipboard: SystemTextClipboard())
        var settings = ProcessingSettings()
        settings.texturePriority = painting()
        func inspect(_ id: String) -> Inspection {
            Inspection(
                projectRoot: workspace.root,
                project: ProjectData(id: id, name: id, revision: 1, images: [], settings: settings, hints: []),
                run: nil, reconstruction: nil, cloud: nil)
        }
        workspace.inspection = inspect("one")
        let editor = workspace.texturePriorityEditor
        editor.followRun(settings.texturePriority, alignment: "alignment-a")
        editor.append(painting().strokes[0])
        let draft = editor.painting
        settings.exclusionRegions = [ExclusionRegion()]
        workspace.inspection = inspect("one")
        XCTAssertTrue(workspace.texturePriorityEditor === editor)
        XCTAssertEqual(editor.painting, draft)
        XCTAssertEqual(editor.undoHistory.count, 1)
        settings.texturePriority = nil
        workspace.inspection = inspect("two")
        XCTAssertTrue(editor.painting.strokes.isEmpty)
        XCTAssertTrue(editor.undoHistory.isEmpty)
    }

    @MainActor
    func testUndoRedoClearAndAlignmentProtection() {
        let model = TexturePriorityEditorModel()
        model.load(nil, alignment: "alignment-a")
        model.append(painting().strokes[0])
        XCTAssertEqual(model.painting.strokes.count, 1)
        model.undo()
        XCTAssertNil(model.savedValue)
        model.redo()
        XCTAssertEqual(model.painting.strokes.count, 1)
        model.clear()
        XCTAssertNil(model.savedValue)
        model.undo()
        XCTAssertEqual(model.painting.strokes.count, 1)
        model.load(painting(), alignment: "alignment-b")
        XCTAssertTrue(model.incompatible)
        model.append(painting().strokes[0])
        XCTAssertEqual(model.painting.strokes.count, 1)
        model.clear()
        XCTAssertFalse(model.incompatible)
        XCTAssertEqual(model.painting.alignmentArtifact, "alignment-b")
    }

    @MainActor
    func testBrushPicksFrontSurfaceCommitsOneStrokeAndPreservesCameraAndGeometry() async throws {
        let scene = NativeSceneFactory().make(
            reconstruction: nil,
            points: [
                CloudPoint(position: [0, 0, 0], colour: nil, error: 0),
                CloudPoint(position: [0, 0, -1], colour: nil, error: 0),
            ],
            photos: [], root: URL(fileURLWithPath: "/tmp"))
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let geometry = artifact.geometry
        let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 400, height: 300))
        view.scene = scene
        view.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
        view.pointOfView?.position = SCNVector3(0, 0, 5)
        view.pointOfView?.look(at: SCNVector3Zero)
        view.needsInitialFit = false
        let window = NSWindow(contentRect: view.frame, styleMask: [.titled], backing: .buffered, defer: false)
        window.contentView = view
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        try await Task.sleep(for: .milliseconds(250))
        view.textureBrush.configuration = TextureBrushConfiguration()
        view.textureBrush.painting = TexturePriorityPainting(alignmentArtifact: "alignment-a")
        let camera = view.pointOfView?.position
        let projected = view.projectPoint(SCNVector3Zero)
        let location = NSPoint(x: projected.x, y: projected.y)
        let near = view.unprojectPoint(SCNVector3(projected.x, projected.y, 0))
        let far = view.unprojectPoint(SCNVector3(projected.x, projected.y, 1))
        let picked = CloudPointPicker().pick(
            at: location, in: view, node: artifact, pixelRadius: 6, preferFrontmost: true)
        XCTAssertNotNil(picked, "Projected \(projected), near \(near), far \(far)")
        let sample = try XCTUnwrap(
            view.textureBrush.sample(at: location, in: view), "Picked \(String(describing: picked))")
        XCTAssertEqual(sample.centre[2], 0, accuracy: 1e-8)
        XCTAssertEqual(sample.depth, sample.radius / 4, accuracy: 1e-8)
        var strokes: [TexturePriorityStroke] = []
        view.textureBrush.finished = { strokes.append($0) }
        XCTAssertTrue(view.textureBrush.begin(at: location, in: view))
        XCTAssertTrue(view.textureBrush.drag(to: location, in: view))
        XCTAssertTrue(view.textureBrush.end(in: view))
        XCTAssertEqual(strokes.count, 1)
        XCTAssertEqual(strokes[0].samples.count, 1)
        XCTAssertEqual(view.pointOfView?.position.z, camera?.z)
        XCTAssertTrue(artifact.geometry === geometry)
        XCTAssertTrue(view.textureBrush.begin(at: location, in: view))
        XCTAssertTrue(view.textureBrush.cancel(in: view))
        XCTAssertEqual(strokes.count, 1, "Cancel must not save an incomplete stroke")
    }

    @MainActor
    func testSavedCloudPriorityPreview() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_PRIORITY_PROJECT"], let runID = environment["ECHO_TEST_PRIORITY_RUN"]
        else {
            throw XCTSkip("Set ECHO_TEST_PRIORITY_PROJECT and ECHO_TEST_PRIORITY_RUN to inspect a saved point cloud.")
        }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run = try await SavedRunFixture.read(root, id: runID)
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let presentation = try await reader.load(
            root: root, run: run, stage: .cloud, unit: nil, mode: "All support", slice: 0.5)
        let scene = try XCTUnwrap(presentation.scene)
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let original = artifact.geometry
        let source = try XCTUnwrap(CropPointSource(node: artifact))
        let point = source.data.withUnsafeBytes { source.position(source.count / 2, bytes: $0) }
        let bounds = artifact.boundingBox
        let radius = max(bounds.max.x - bounds.min.x, bounds.max.y - bounds.min.y) / 5
        let sample = TextureBrushSample(
            centre: [point.x, point.y, point.z], normal: [0, 0, 1], radius: radius, depth: radius / 4)
        let painting = TexturePriorityPainting(
            alignmentArtifact: try XCTUnwrap(run.artifacts["alignment"]),
            strokes: [TexturePriorityStroke(weight: 4, samples: [sample])])
        let preview = try TexturePriorityPreview(source: source, strokes: painting.strokes)
        XCTAssertGreaterThan(preview.positions.count, 0)
        XCTAssertLessThan(preview.positions.count, source.count)
        let controller = SceneViewportController()
        let domain = "echo-priority-preview-\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: domain))
        defer { defaults.removePersistentDomain(forName: domain) }
        let workspace = WorkspaceViewModel(
            client: CLIProjectClient(cli: TestProjectManagementCLIClient()), project: project,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults), clipboard: SystemTextClipboard())
        workspace.inspection = Inspection(
            projectRoot: project, project: run.snapshot, run: run, reconstruction: nil, cloud: nil)
        let editor = TexturePriorityEditorModel()
        editor.load(painting, alignment: painting.alignmentArtifact)
        let host = NSHostingView(
            rootView: HStack {
                Artifact3DViewer(
                    presentation: presentation, openFeatures: { _ in }, controller: controller,
                    showGround: .constant(false),
                    texturePainting: painting, textureBrush: TextureBrushConfiguration(), showTexturePriority: true)
                Divider()
                TexturePriorityEditor(workspace: workspace, model: editor).frame(width: 280)
            }.padding(12).background(Color(nsColor: .windowBackgroundColor)))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 1100, height: 800), styleMask: [.titled], backing: .buffered,
            defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        for _ in 0..<100 {
            if scene.rootNode.childNode(withName: "texture-priority-preview", recursively: false) != nil { break }
            try await Task.sleep(for: .milliseconds(100))
        }
        XCTAssertNotNil(scene.rootNode.childNode(withName: "texture-priority-preview", recursively: false))
        XCTAssertTrue(
            artifact.geometry === original, "Painting preview must preserve the saved point geometry and colours")
        let view = try XCTUnwrap(controller.view)
        let projected = view.projectPoint(SCNVector3(point.x, point.y, point.z))
        XCTAssertNotNil(view.textureBrush.sample(at: NSPoint(x: projected.x, y: projected.y), in: view))
        if let output = environment["ECHO_TEST_PRIORITY_RENDER"] {
            let panel = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
            host.cacheDisplay(in: host.bounds, to: panel)
            try XCTUnwrap(panel.representation(using: .png, properties: [:])).write(
                to: URL(fileURLWithPath: output + ".ui.png"))
            let renderer = SCNRenderer(device: try XCTUnwrap(MTLCreateSystemDefaultDevice()), options: nil)
            renderer.scene = scene
            renderer.pointOfView = controller.view?.pointOfView
            let image = renderer.snapshot(
                atTime: 0, with: CGSize(width: 1100, height: 800), antialiasingMode: .multisampling4X)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: URL(fileURLWithPath: output))
        }
    }

    private func painting() -> TexturePriorityPainting {
        TexturePriorityPainting(
            alignmentArtifact: "alignment-a",
            strokes: [
                TexturePriorityStroke(
                    weight: 4,
                    samples: [
                        TextureBrushSample(centre: [0, 0, 0], normal: [0, 0, 1], radius: 2, depth: 0.5)
                    ])
            ])
    }
}
