import AppKit
import EchoRendering
import MapKit
import Metal
import SceneKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class GeographicProbeTests: XCTestCase {
    func testInputRejectsInvalidCoordinatesHeightsAndAccuracy() throws {
        XCTAssertThrowsError(try GeographicProbeInput(latitude: "nan", longitude: "150", altitude: "", accuracy: "5"))
        XCTAssertThrowsError(try GeographicProbeInput(latitude: "91", longitude: "150", altitude: "", accuracy: "5"))
        XCTAssertThrowsError(try GeographicProbeInput(latitude: "0", longitude: "181", altitude: "", accuracy: "5"))
        XCTAssertThrowsError(try GeographicProbeInput(latitude: "0", longitude: "0", altitude: "inf", accuracy: "5"))
        XCTAssertThrowsError(try GeographicProbeInput(latitude: "0", longitude: "0", altitude: "", accuracy: "-1"))
        let input = try GeographicProbeInput(latitude: " -33.7 ", longitude: "150", altitude: "", accuracy: "0")
        XCTAssertNil(input.altitude)
        XCTAssertEqual(input.latitude, -33.7)
    }

    func testSurfacePlacementSelectsTopmostIntersectionAndRejectsMissingSurface() throws {
        let artifact = fixture()
        let input = try GeographicProbeInput(latitude: "-33.7", longitude: "150", altitude: "", accuracy: "5")
        let result = try GeographicProbeResolver().resolve(
            input, origin: XCTUnwrap(artifact.reconstruction.origin), bounds: artifact.bounds,
            surface: XCTUnwrap(artifact.surface))
        XCTAssertEqual(result.local.y, 10, accuracy: 0.0001)
        XCTAssertEqual(result.surfaceIntersections, 2)
        XCTAssertEqual(result.coordinate.latitude, -33.7, accuracy: 1e-8)
        XCTAssertEqual(result.coordinate.longitude, 150, accuracy: 1e-8)
        XCTAssertEqual(try XCTUnwrap(result.coordinate.altitude), 110, accuracy: 0.0001)
        let outside = try GeographicProbeInput(latitude: "-33.8", longitude: "150", altitude: "", accuracy: "5")
        XCTAssertThrowsError(
            try GeographicProbeResolver().resolve(
                outside, origin: XCTUnwrap(artifact.reconstruction.origin), bounds: artifact.bounds,
                surface: XCTUnwrap(artifact.surface)))
    }

    func testEnteredAltitudeUsesTheModelDatumAndRequiresKnownReference() throws {
        let artifact = fixture()
        let input = try GeographicProbeInput(latitude: "-33.7", longitude: "150", altitude: "105", accuracy: "3")
        let result = try GeographicProbeResolver().resolve(
            input, origin: XCTUnwrap(artifact.reconstruction.origin), bounds: artifact.bounds,
            surface: XCTUnwrap(artifact.surface))
        XCTAssertEqual(result.local.y, 5, accuracy: 0.0001)
        XCTAssertEqual(result.surfaceIntersections, 0)
        XCTAssertThrowsError(
            try GeographicProbeResolver().resolve(
                input, origin: GeoPosition(latitude: -33.7, longitude: 150, altitude: 100, altitudeDatum: "unknown"),
                bounds: artifact.bounds, surface: XCTUnwrap(artifact.surface)))
    }

    @MainActor
    func testProbeReplacesItsMarkerWithoutChangingGeometryAndKeepsMissesOnTheMap() throws {
        let artifact = fixture()
        let scene = try XCTUnwrap(artifact.presentation.scene)
        let node = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let geometry = node.geometry
        let model = GeographicProbeViewModel()
        model.latitude = "-33.7"
        model.longitude = "150"
        for radius in ["5", "8", "0"] {
            model.accuracy = radius
            model.show(in: artifact)
            XCTAssertNil(model.error)
            XCTAssertEqual(scene.rootNode.childNodes.filter { $0.name == "geographic-test-location" }.count, 1)
        }
        XCTAssertTrue(node.geometry === geometry)
        model.latitude = "-33.8"
        model.show(in: artifact)
        XCTAssertNil(model.result)
        XCTAssertNotNil(model.error)
        XCTAssertEqual(model.mapLocation?.latitude, -33.8)
        XCTAssertNil(scene.rootNode.childNode(withName: "geographic-test-location", recursively: false))
        model.pickModel(SCNVector3(0, 0, 0), in: artifact)
        XCTAssertNil(model.error)
        XCTAssertEqual(model.result?.placement, "Picked model surface")
        model.accuracy = "-1"
        model.pickModel(SCNVector3(0, 0, 0), in: artifact)
        XCTAssertNotNil(model.error)
        XCTAssertNil(model.result)
        model.clear(in: artifact)
        XCTAssertNil(model.mapLocation)
    }

    @MainActor
    func testMapCameraRefreshRetainsProbeAndAccuracyCircle() {
        let map = MKMapView()
        let coordinator = PhotoMapCoordinator(selection: { _ in })
        coordinator.updateProbe(map: map, position: GeoPosition(latitude: -33.7, longitude: 150), accuracy: 5)
        coordinator.update(map: map, cameras: [], identity: "new-view", selected: nil)
        XCTAssertEqual(map.annotations.filter { $0 is GeographicProbeAnnotation }.count, 1)
        XCTAssertEqual(map.overlays.filter { $0 is MKCircle }.count, 1)
        coordinator.updateProbe(map: map, position: nil, accuracy: 0)
        XCTAssertFalse(map.annotations.contains { $0 is GeographicProbeAnnotation })
        XCTAssertFalse(map.overlays.contains { $0 is MKCircle })
    }

    @MainActor
    func testProbeOnSavedModelAndNativeRender() async throws {
        let env = ProcessInfo.processInfo.environment
        guard let project = env["ECHO_TEST_PROJECT"], let runID = env["ECHO_TEST_RUN"] else {
            throw XCTSkip("Set ECHO_TEST_PROJECT and ECHO_TEST_RUN for a saved geographic probe.")
        }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run: RunData = try json.read(root.appendingPathComponent("runs/\(runID)/run.json"))
        let artifact = try GeoreferenceArtifactReader().load(root: root, run: run)
        let meshPath = try XCTUnwrap(run.artifacts["mesh"])
        let surface: Surface = try json.read(root.appendingPathComponent(meshPath).appendingPathComponent("mesh.json"))
        let face = surface.faces[surface.faces.count / 2]
        let local = SCNVector3(
            face.map { surface.vertices[$0][0] }.reduce(0, +) / 3,
            face.map { surface.vertices[$0][1] }.reduce(0, +) / 3,
            face.map { surface.vertices[$0][2] }.reduce(0, +) / 3)
        let model = GeographicProbeViewModel()
        model.pickModel(local, in: artifact)
        let picked = try XCTUnwrap(model.result)
        let restored = GeographicTransform(origin: try XCTUnwrap(artifact.reconstruction.origin)).local(
            picked.coordinate)
        if picked.coordinate.altitude != nil {
            XCTAssertEqual(restored.x, local.x, accuracy: 0.001)
            XCTAssertEqual(restored.y, local.y, accuracy: 0.001)
            XCTAssertEqual(restored.z, local.z, accuracy: 0.001)
        }
        model.show(in: artifact)
        XCTAssertNil(model.error)
        let result = try XCTUnwrap(model.result)
        XCTAssertEqual(result.coordinate.latitude, picked.coordinate.latitude, accuracy: 1e-7)
        XCTAssertEqual(result.coordinate.longitude, picked.coordinate.longitude, accuracy: 1e-7)
        if let output = env["ECHO_GEO_PROBE_REVIEW_OUTPUT"] {
            let scene = try XCTUnwrap(artifact.presentation.scene)
            let camera = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
            let renderer = SCNRenderer(device: try XCTUnwrap(MTLCreateSystemDefaultDevice()))
            renderer.scene = scene
            renderer.pointOfView = camera
            let image = renderer.snapshot(
                atTime: 0, with: CGSize(width: 1440, height: 1000), antialiasingMode: .multisampling4X)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
            let folder = URL(fileURLWithPath: output)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: folder.appendingPathComponent("gps-probe.png"))
            let controller = SceneViewportController()
            let host = NSHostingView(
                rootView: GeoreferenceProbeView(
                    artifact: artifact, probe: model, controller: controller, showGround: .constant(false)
                )
                .padding(16).environment(\.colorScheme, .light).background(Color.white))
            let window = NSWindow(
                contentRect: CGRect(x: 0, y: 0, width: 1036, height: 640),
                styleMask: [.titled, .resizable], backing: .buffered, defer: false)
            window.contentView = host
            window.orderFront(nil)
            defer { window.orderOut(nil) }
            host.layoutSubtreeIfNeeded()
            try await Task.sleep(for: .milliseconds(400))
            let view = try XCTUnwrap(controller.view)
            let location = CGPoint(x: view.bounds.midX, y: view.bounds.midY)
            let node = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
            XCTAssertFalse(view.hitTest(location, options: [.rootNode: node, .backFaceCulling: false]).isEmpty)
            model.clear(in: artifact)
            for type in [NSEvent.EventType.leftMouseDown, .leftMouseUp] {
                let event = try XCTUnwrap(
                    NSEvent.mouseEvent(
                        with: type, location: view.convert(location, to: nil), modifierFlags: [],
                        timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                        context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
                if type == .leftMouseDown { view.mouseDown(with: event) } else { view.mouseUp(with: event) }
            }
            XCTAssertEqual(model.result?.placement, "Picked model surface")
            let pickedPoint = try XCTUnwrap(model.result?.local)
            controller.focus(on: pickedPoint, radius: 10)
            XCTAssertEqual(view.defaultCameraController.target.x, pickedPoint.x, accuracy: 0.001)
            controller.reset()
            try await Task.sleep(for: .milliseconds(300))
            host.layoutSubtreeIfNeeded()
            let ui = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
            host.cacheDisplay(in: host.bounds, to: ui)
            try XCTUnwrap(ui.representation(using: .png, properties: [:])).write(
                to: folder.appendingPathComponent("gps-controls.png"))
            let map = try XCTUnwrap(mapView(in: host))
            let coordinate = try XCTUnwrap(model.result?.coordinate)
            model.clear(in: artifact)
            try await Task.sleep(for: .milliseconds(100))
            host.layoutSubtreeIfNeeded()
            let mapPoint = map.convert(
                CLLocationCoordinate2D(latitude: coordinate.latitude, longitude: coordinate.longitude),
                toPointTo: map)
            let pixel = map.convert(CGPoint(x: mapPoint.x + 1, y: mapPoint.y + 1), toCoordinateFrom: map)
            for type in [NSEvent.EventType.leftMouseDown, .leftMouseUp] {
                let event = try XCTUnwrap(
                    NSEvent.mouseEvent(
                        with: type, location: map.convert(mapPoint, to: nil), modifierFlags: [],
                        timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                        context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
                window.sendEvent(event)
            }
            try await Task.sleep(for: .milliseconds(300))
            XCTAssertNil(model.error)
            XCTAssertEqual(
                try XCTUnwrap(model.result?.coordinate.latitude), coordinate.latitude,
                accuracy: max(1e-8, abs(pixel.latitude - coordinate.latitude)))
            XCTAssertEqual(
                try XCTUnwrap(model.result?.coordinate.longitude), coordinate.longitude,
                accuracy: max(1e-8, abs(pixel.longitude - coordinate.longitude)))
        }
    }

    @MainActor
    private func mapView(in view: NSView) -> MKMapView? {
        if let map = view as? MKMapView { return map }
        for child in view.subviews {
            if let map = mapView(in: child) { return map }
        }
        return nil
    }

    private func fixture() -> GeoreferenceArtifact {
        let surface = Surface(
            vertices: [
                [-10, 0, -10], [10, 0, -10], [10, 0, 10], [-10, 0, 10],
                [-10, 10, -10], [10, 10, -10], [10, 10, 10], [-10, 10, 10],
            ],
            faces: [[0, 1, 2], [0, 2, 3], [4, 5, 6], [4, 6, 7]], sourceImages: [])
        let reconstruction = ReconstructionScene(
            frame: "East/Up/South",
            origin: GeoPosition(latitude: -33.7, longitude: 150, altitude: 100, altitudeDatum: "WGS84 ellipsoid"),
            cameras: [], points: [], unplaced: [], warnings: [])
        let scene = NativeSceneFactory().make(
            reconstruction: reconstruction, points: [], photos: [], root: URL(fileURLWithPath: "/tmp"), surface: surface
        )
        return GeoreferenceArtifact(
            reconstruction: reconstruction,
            presentation: ArtifactPresentation(title: "test", detail: "", scene: scene, sceneIdentity: "test"),
            overlay: nil, cameras: [], surface: surface, bounds: SceneBounds(positions: surface.vertices))
    }
}
