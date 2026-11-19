import AppKit
import SceneKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class SparseCameraRefreshTests: XCTestCase {
    @MainActor
    func testLiveRefreshKeepsSelectedCameraAndViewButClearsForAnotherRun() async throws {
        let photo = try JSONDecoder().decode(
            Photo.self,
            from: Data(
                """
                {"id":"camera-a","name":"a.jpg","source":"missing.jpg","preview":"missing.jpg",
                 "width":100,"height":80,"camera":"test","focalPixels":50,"enabled":true,"sets":[],"metadata":{}}
                """.utf8))
        let controller = SceneViewportController()
        func presentation(_ iteration: Int, identity: String = "run-alignment", georeferenced: Bool = false)
            -> ArtifactPresentation
        {
            let camera = CameraPose(
                imageId: photo.id, centre: [Double(iteration) * 0.1, 0, -2],
                rotation: [1, 0, 0, 0, 1, 0, 0, 0, 1], focalPixels: 50, source: "test")
            let points = [CloudPoint(position: georeferenced ? [100, 200, 300] : [0, 0, 0], colour: nil, error: 0)]
            let reconstruction = ReconstructionScene(
                frame: "local", origin: nil, cameras: [camera], points: points, unplaced: [], warnings: [])
            let root = URL(fileURLWithPath: "/tmp")
            return ArtifactPresentation(
                title: "Live alignment", detail: "Iteration \(iteration)",
                scene: NativeSceneFactory().make(
                    reconstruction: reconstruction, points: points, photos: [photo], root: root),
                sceneIdentity: identity,
                sparseInspection: SparseInspectionData(
                    root: root, scene: reconstruction, photos: [photo], featureFiles: [:]),
                metricScale: georeferenced)
        }
        func viewer(_ value: ArtifactPresentation) -> Artifact3DViewer {
            Artifact3DViewer(
                presentation: value, openFeatures: { _ in }, controller: controller, showGround: .constant(false))
        }
        let initial = presentation(0)
        let host = NSHostingView(rootView: viewer(initial))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 1200, height: 800),
            styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        let view = try XCTUnwrap(controller.view)
        view.selection?(.camera(photo.id))
        try await Task.sleep(for: .milliseconds(100))
        host.layoutSubtreeIfNeeded()
        let transform = try XCTUnwrap(view.pointOfView?.simdTransform)
        let width = view.bounds.width
        for iteration in 1...5 {
            let updated = presentation(iteration)
            host.rootView = viewer(updated)
            host.layoutSubtreeIfNeeded()
            try await Task.sleep(for: .milliseconds(100))
            let marker = try XCTUnwrap(
                updated.scene?.rootNode.childNode(withName: "sparse-selection", recursively: false))
            XCTAssertEqual(marker.position.x, Double(iteration) * 0.1, accuracy: 0.0001)
            XCTAssertEqual(view.bounds.width, width, "The camera inspector must stay open")
            XCTAssertEqual(view.pointOfView?.simdTransform, transform, "Live refresh must preserve the viewing angle")
        }
        let referenced = presentation(6, georeferenced: true)
        host.rootView = viewer(referenced)
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        host.layoutSubtreeIfNeeded()
        XCTAssertNotEqual(
            view.pointOfView?.simdTransform, transform,
            "Geographic referencing changes the frame; the arbitrary preview's viewing transform is no longer valid")
        XCTAssertEqual(view.defaultCameraController.target.y, 200, accuracy: 0.01)
        XCTAssertNotNil(
            referenced.scene?.rootNode.childNode(withName: "sparse-selection", recursively: false),
            "Referencing must keep the selected source camera")
        let referencedTransform = view.pointOfView?.simdTransform
        host.rootView = viewer(presentation(7, georeferenced: true))
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        XCTAssertEqual(
            view.pointOfView?.simdTransform, referencedTransform, "Later geographic updates preserve the view")
        let anotherRun = presentation(9, identity: "another-run")
        host.rootView = viewer(anotherRun)
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        XCTAssertNil(anotherRun.scene?.rootNode.childNode(withName: "sparse-selection", recursively: false))
    }
}
