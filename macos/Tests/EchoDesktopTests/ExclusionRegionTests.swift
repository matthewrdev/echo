import AppKit
import Metal
import SceneKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class ExclusionRegionTests: XCTestCase {
    func testSettingsRoundTripAndInvalidateCloud() throws {
        let original = try JSONDecoder().decode(ProcessingSettings.self, from: Data("{}".utf8))
        XCTAssertTrue(original.exclusionRegions.isEmpty)
        var settings = original
        settings.exclusionRegions = [ExclusionRegion(), ExclusionRegion(shape: .cylinder)]
        let restored = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(restored.exclusionRegions, settings.exclusionRegions)
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: restored), .cloud)
        var moved = settings
        moved.exclusionRegions[0].centreX = 2
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: settings, to: moved), .cloud)
        moved = settings
        moved.exclusionRegions[1].enabled = false
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: settings, to: moved), .cloud)
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: settings, to: original), .cloud)
    }

    func testRegionPlacementDimensionsAndValidation() {
        let bounds = SceneBounds(positions: [[-10, -2, -4], [10, 8, 4]])
        var region = ExclusionRegion(shape: .cylinder, bounds: bounds)
        XCTAssertEqual(region.centreY, 3)
        XCTAssertEqual(region.height, 5)
        XCTAssertEqual(region.radius, 2.5)
        region.place(at: SCNVector3(1, 2, 3))
        XCTAssertEqual(region.centreX, 1)
        XCTAssertEqual(region.centreY, 2)
        XCTAssertEqual(region.centreZ, 3)
        XCTAssertTrue(region.isValid)
        region.radius = 0
        XCTAssertFalse(region.isValid)
        region.radius = 1
        region.centreX = .nan
        XCTAssertFalse(region.isValid)
    }

    @MainActor
    func testOverlayTracksEditsAndDoesNotChangeArtifactBounds() throws {
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [CloudPoint(position: [0, 0, 0], colour: nil, error: 0)], photos: [],
            root: URL(fileURLWithPath: "/tmp"))
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let bounds = artifact.boundingBox
        var box = ExclusionRegion()
        var cylinder = ExclusionRegion(shape: .cylinder)
        cylinder.centreX = 3
        let overlay = ExclusionSceneOverlay()
        overlay.update(scene: scene, regions: [box, cylinder], selectedID: box.id)
        let root = try XCTUnwrap(scene.rootNode.childNode(withName: "exclusion-regions", recursively: false))
        XCTAssertEqual(root.childNodes.count, 4)
        XCTAssertTrue(root.childNode(withName: box.id, recursively: false)?.geometry is SCNBox)
        XCTAssertTrue(root.childNode(withName: cylinder.id, recursively: false)?.geometry is SCNCylinder)
        XCTAssertEqual(artifact.boundingBox.min.x, bounds.min.x)
        XCTAssertEqual(artifact.boundingBox.max.x, bounds.max.x)
        box.enabled = false
        cylinder.height = 7
        overlay.update(scene: scene, regions: [box, cylinder], selectedID: cylinder.id)
        let updated = try XCTUnwrap(scene.rootNode.childNode(withName: "exclusion-regions", recursively: false))
        XCTAssertEqual(updated.childNodes.count, 2)
        XCTAssertEqual((updated.childNodes.first?.geometry as? SCNCylinder)?.height, 7)
        overlay.update(scene: scene, regions: [], selectedID: nil)
        XCTAssertNil(scene.rootNode.childNode(withName: "exclusion-regions", recursively: false))
    }

    @MainActor
    func testPlacementPicksCloudAndMeshWithoutPickingTheOverlay() async throws {
        for mesh in [false, true] {
            let surface = Surface(
                vertices: [[-1, -1, 0], [1, -1, 0], [0, 1, 0]], faces: [[0, 1, 2]], sourceImages: [])
            let scene = NativeSceneFactory().make(
                reconstruction: nil,
                points: mesh ? [] : [CloudPoint(position: [0, 0, 0], colour: nil, error: 0)],
                photos: [], root: URL(fileURLWithPath: "/tmp"), surface: mesh ? surface : nil)
            let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 400, height: 300))
            view.scene = scene
            view.pointOfView = scene.rootNode.childNode(withName: "viewer-camera", recursively: false)
            view.fitArtifact()
            let overlay = ExclusionSceneOverlay()
            overlay.update(scene: scene, regions: [ExclusionRegion()], selectedID: nil)
            let projected = view.projectPoint(SCNVector3(0, 0, 0))
            let picked = try XCTUnwrap(view.exclusionPoint(at: NSPoint(x: projected.x, y: projected.y)))
            XCTAssertEqual(picked.x, 0, accuracy: 0.001)
            XCTAssertEqual(picked.y, 0, accuracy: 0.001)
            XCTAssertEqual(picked.z, 0, accuracy: 0.001, "Placement must hit the artifact, not the exclusion volume")
        }
    }

    @MainActor
    func testSavedCloudDisplaysBothExclusionShapes() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_CROP_PROJECT"], let runID = environment["ECHO_TEST_CROP_RUN"] else {
            throw XCTSkip("Set ECHO_TEST_CROP_PROJECT and ECHO_TEST_CROP_RUN to inspect exclusions on saved artifacts.")
        }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run: RunData = try json.read(root.appendingPathComponent("runs/\(runID)/run.json"))
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let presentation = try await reader.load(
            root: root, run: run, stage: .cloud, unit: nil, mode: "All support", slice: 0.5)
        let scene = try XCTUnwrap(presentation.scene)
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let box = artifact.boundingBox
        let bounds = SceneBounds(positions: [[box.min.x, box.min.y, box.min.z], [box.max.x, box.max.y, box.max.z]])
        var cylinder = ExclusionRegion(shape: .cylinder, bounds: bounds)
        cylinder.centreX += bounds.span / 5
        let region = ExclusionRegion(bounds: bounds)
        let controller = SceneViewportController()
        let host = NSHostingView(
            rootView: HStack {
                Artifact3DViewer(
                    presentation: presentation, openFeatures: { _ in }, controller: controller,
                    showGround: .constant(false),
                    exclusionRegions: [region, cylinder], selectedExclusionID: region.id)
                ExclusionRegionEditor(region: .constant(region), bounds: bounds, metricScale: presentation.metricScale)
                    .padding().frame(width: 280)
            })
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 1200, height: 800), styleMask: [.titled], backing: .buffered,
            defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(300))
        XCTAssertEqual(scene.rootNode.childNode(withName: "exclusion-regions", recursively: false)?.childNodes.count, 4)
        if let output = environment["ECHO_TEST_CROP_RENDER"] {
            let folder = URL(fileURLWithPath: output)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let bitmap = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
            host.cacheDisplay(in: host.bounds, to: bitmap)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: folder.appendingPathComponent("exclusions.png"))
            let renderer = SCNRenderer(device: try XCTUnwrap(MTLCreateSystemDefaultDevice()), options: nil)
            renderer.scene = scene
            renderer.pointOfView = controller.view?.pointOfView
            let rendered = renderer.snapshot(
                atTime: 0, with: CGSize(width: 1200, height: 800), antialiasingMode: .multisampling4X)
            let sceneBitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(rendered.tiffRepresentation)))
            try XCTUnwrap(sceneBitmap.representation(using: .png, properties: [:])).write(
                to: folder.appendingPathComponent("exclusions-scene.png"))
        }
    }
}
