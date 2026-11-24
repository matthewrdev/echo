import AppKit
import EchoRendering
import SceneKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class CameraNavigationStressTests: XCTestCase {
    @MainActor
    func testRapidCameraKeysWhileBothCloudViewsRender() async throws {
        try WindowedTests.requireOptIn()

        let photos = try (0..<8).map { index in
            try JSONDecoder().decode(
                Photo.self,
                from: Data(
                    """
                    {"id":"camera-\(index)","name":"\(index).jpg","source":"missing.jpg","preview":"missing.jpg",
                     "width":100,"height":80,"camera":"test","focalPixels":50,"enabled":true,"sets":[],"metadata":{}}
                    """.utf8))
        }
        let cameras = photos.enumerated().map { index, photo in
            CameraPose(
                imageId: photo.id, centre: [Double(index) * 0.1, 0, -2],
                rotation: [1, 0, 0, 0, 1, 0, 0, 0, 1], focalPixels: 50, source: "test")
        }
        let points = (0..<10000).map { index in
            CloudPoint(
                position: [Double(index % 100) / 50 - 1, Double(index / 100) / 50 - 1, 0],
                colour: Data([80, 210, 185]), error: 0)
        }
        let root = URL(fileURLWithPath: "/tmp")
        let reconstruction = ReconstructionScene(
            frame: "local", origin: nil, cameras: cameras, points: [], unplaced: [], warnings: [])
        let scene = NativeSceneFactory().make(
            reconstruction: reconstruction, points: points, photos: photos, root: root)
        let data = CameraInspectionData(root: root, cameras: cameras, photos: photos)
        let presentation = ArtifactPresentation(
            title: "Cloud", detail: "", scene: scene, sceneIdentity: "camera-navigation-stress", cameraInspection: data)
        try await exercise(presentation)
    }

    @MainActor
    func testRapidCameraKeysWithSavedCloud() async throws {
        try WindowedTests.requireOptIn()

        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_CAMERA_PROJECT"], let runID = environment["ECHO_TEST_CAMERA_RUN"]
        else {
            throw XCTSkip("Set ECHO_TEST_CAMERA_PROJECT and ECHO_TEST_CAMERA_RUN to stress a saved cloud.")
        }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run = try await SavedRunFixture.read(root, id: runID)
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let presentation = try await reader.load(root: root, run: run, stage: .cloud, unit: nil, mode: "", slice: 0.5)
        try await exercise(presentation)
    }

    @MainActor
    private func exercise(_ presentation: ArtifactPresentation) async throws {
        let scene = try XCTUnwrap(presentation.scene)
        let data = try XCTUnwrap(presentation.cameraInspection)
        XCTAssertGreaterThan(data.cameraOrder.count, 6)
        let startID = data.cameraOrder[3]
        let endID = data.cameraOrder[6]
        let start = try XCTUnwrap(data.frustums[startID])
        let end = try XCTUnwrap(data.frustums[endID])
        let controller = SceneViewportController()
        let host = NSHostingView(
            rootView: Artifact3DViewer(
                presentation: presentation, openFeatures: { _ in }, controller: controller,
                showGround: .constant(true)))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 1200, height: 800),
            styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        let main = try XCTUnwrap(controller.view)
        main.rendersContinuously = true
        main.selection?(.camera(startID))
        try await Task.sleep(for: .milliseconds(100))
        let preview = try XCTUnwrap(cameraPreview(in: host))
        preview.rendersContinuously = true
        let previewScene = try XCTUnwrap(preview.scene)
        let previewCamera = try XCTUnwrap(preview.pointOfView)
        let marker = try XCTUnwrap(scene.rootNode.childNode(withName: "sparse-selection", recursively: false))
        let left = try key(123, window: window)
        let right = try key(124, window: window)
        for iteration in 0..<120 {
            for _ in 0..<3 { main.keyDown(with: right) }
            try await Task.sleep(for: .milliseconds(20))
            host.layoutSubtreeIfNeeded()
            XCTAssertEqual(preview.pointOfView?.position.x ?? .infinity, end.centre.x, accuracy: 0.0001)
            XCTAssertTrue(preview.scene === previewScene)
            XCTAssertTrue(preview.pointOfView === previewCamera)
            XCTAssertTrue(scene.rootNode.childNode(withName: "sparse-selection", recursively: false) === marker)
            _ = preview.snapshot()
            for _ in 0..<3 { preview.keyDown(with: left) }
            try await Task.sleep(for: .milliseconds(20))
            host.layoutSubtreeIfNeeded()
            if iteration.isMultiple(of: 10) {
                _ = main.snapshot()
                _ = preview.snapshot()
            }
        }
        XCTAssertEqual(preview.pointOfView?.position.x ?? .infinity, start.centre.x, accuracy: 0.0001)
        XCTAssertNotNil(scene.rootNode.childNode(withName: "artifact", recursively: false))
        XCTAssertNotNil(scene.rootNode.childNode(withName: "sparse-selection", recursively: false))
        if let output = ProcessInfo.processInfo.environment["ECHO_TEST_CAMERA_RENDER"] {
            let folder = URL(fileURLWithPath: output)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try writeSnapshot(main, to: folder.appendingPathComponent("cloud-camera-stress.png"))
            try writeSnapshot(preview, to: folder.appendingPathComponent("cloud-camera-stress-preview.png"))
        }
        preview.keyDown(with: try key(53, window: window))
        try await Task.sleep(for: .milliseconds(100))
        XCTAssertNil(cameraPreview(in: host))
        XCTAssertNil(scene.rootNode.childNode(withName: "sparse-selection", recursively: false))
    }

    @MainActor
    private func writeSnapshot(_ view: SCNView, to file: URL) throws {
        let data = try XCTUnwrap(view.snapshot().tiffRepresentation)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: data))
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: file)
    }

    @MainActor
    private func cameraPreview(in view: NSView) -> SourceCameraSceneView? {
        if let preview = view as? SourceCameraSceneView { return preview }
        for child in view.subviews {
            if let preview = cameraPreview(in: child) { return preview }
        }
        return nil
    }

    private func key(_ code: UInt16, window: NSWindow) throws -> NSEvent {
        try XCTUnwrap(
            NSEvent.keyEvent(
                with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: window.windowNumber,
                context: nil, characters: "", charactersIgnoringModifiers: "", isARepeat: true, keyCode: code))
    }
}
