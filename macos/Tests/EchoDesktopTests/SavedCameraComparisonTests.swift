import AppKit
import SceneKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class SavedCameraComparisonTests: XCTestCase {
    @MainActor
    func testSavedMeshAndTextureCameraComparisonAndKeyboardFocus() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_CAMERA_PROJECT"], let runID = environment["ECHO_TEST_CAMERA_RUN"]
        else {
            throw XCTSkip("Set ECHO_TEST_CAMERA_PROJECT and ECHO_TEST_CAMERA_RUN to inspect saved camera comparisons.")
        }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run: RunData = try json.read(root.appendingPathComponent("runs/\(runID)/run.json"))
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        for stage in [PipelineStage.mesh, .texture] {
            let presentation = try await reader.load(
                root: root, run: run, stage: stage, unit: nil, mode: "Textured", slice: 0.5)
            let data = try XCTUnwrap(presentation.cameraInspection)
            XCTAssertGreaterThan(data.cameraOrder.count, 2)
            let index = data.cameraOrder.count / 2
            let selectedID = data.cameraOrder[index]
            let controller = SceneViewportController()
            let host = NSHostingView(
                rootView: Artifact3DViewer(
                    presentation: presentation, openFeatures: { _ in }, controller: controller,
                    showGround: .constant(false)))
            let window = NSWindow(
                contentRect: CGRect(x: 0, y: 0, width: 1200, height: 800),
                styleMask: [.titled, .resizable], backing: .buffered, defer: false)
            window.contentView = host
            window.orderFront(nil)
            defer { window.orderOut(nil) }
            host.layoutSubtreeIfNeeded()
            try await Task.sleep(for: .milliseconds(200))
            let main = try XCTUnwrap(controller.view)
            main.selection?(.camera(selectedID))
            try await Task.sleep(for: .milliseconds(300))
            let preview = try XCTUnwrap(cameraPreview(in: host))
            assertCamera(preview, matches: try XCTUnwrap(data.frustums[selectedID]))
            if let output = environment["ECHO_TEST_CAMERA_RENDER"] {
                let folder = URL(fileURLWithPath: output)
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let bitmap = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
                host.cacheDisplay(in: host.bounds, to: bitmap)
                try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                    to: folder.appendingPathComponent("\(stage.rawValue)-camera-comparison.png"))
            }
            XCTAssertTrue(window.makeFirstResponder(preview))
            preview.keyDown(with: try key(124, window: window))
            try await Task.sleep(for: .milliseconds(100))
            assertCamera(preview, matches: try XCTUnwrap(data.frustums[data.cameraOrder[index + 1]]))
            main.keyDown(with: try key(123, window: window))
            try await Task.sleep(for: .milliseconds(100))
            assertCamera(preview, matches: try XCTUnwrap(data.frustums[selectedID]))
            preview.keyDown(with: try key(53, window: window))
            try await Task.sleep(for: .milliseconds(100))
            XCTAssertNil(
                cameraPreview(in: host), "Escape must close the comparison while the virtual viewport has focus")
        }
    }

    @MainActor
    private func assertCamera(_ view: SourceCameraSceneView, matches frustum: CameraFrustum) {
        let position = view.pointOfView?.position
        XCTAssertEqual(position?.x ?? .infinity, frustum.centre.x, accuracy: 0.0001)
        XCTAssertEqual(position?.y ?? .infinity, frustum.centre.y, accuracy: 0.0001)
        XCTAssertEqual(position?.z ?? .infinity, frustum.centre.z, accuracy: 0.0001)
        XCTAssertEqual(
            view.pointOfView?.camera?.fieldOfView ?? 0, 2 * atan(frustum.halfHeight) * 180 / .pi, accuracy: 0.0001)
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
                context: nil, characters: "", charactersIgnoringModifiers: "", isARepeat: false, keyCode: code))
    }
}
