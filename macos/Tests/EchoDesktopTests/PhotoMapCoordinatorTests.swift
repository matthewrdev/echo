import MapKit
import XCTest

@testable import EchoDesktop

final class PhotoMapCoordinatorTests: XCTestCase {
    @MainActor
    func testSelectionEnlargesOnlyItsFrustumAndKeepsCameraPositions() throws {
        let cameras = try cameras()
        let coordinator = PhotoMapCoordinator(selection: { _ in })
        let map = MKMapView()
        coordinator.update(map: map, cameras: cameras, identity: "project", selected: nil)
        let annotations = map.annotations.compactMap { $0 as? PhotoMapAnnotation }
        let overlays = map.overlays
        let normalPolygons = overlays.compactMap { $0 as? MKPolygon }
        let normalWidth = try XCTUnwrap(normalPolygons.first).boundingMapRect.width

        coordinator.update(map: map, cameras: cameras, identity: "project", selected: cameras[0].id)

        XCTAssertEqual(map.annotations.count, 2)
        XCTAssertEqual(map.overlays.count, 10)
        XCTAssertTrue(annotations.allSatisfy { original in map.annotations.contains { $0 === original } })
        XCTAssertEqual(overlays.filter { original in map.overlays.contains { $0 === original } }.count, 5)
        let polygons = map.overlays.compactMap { $0 as? MKPolygon }
        XCTAssertEqual(polygons.map { $0.boundingMapRect.width }.max() ?? 0, normalWidth * 8, accuracy: 0.01)
        XCTAssertEqual(polygons.map { $0.boundingMapRect.width }.min() ?? 0, normalWidth, accuracy: 0.01)
        for annotation in annotations {
            let view = try XCTUnwrap(coordinator.mapView(map, viewFor: annotation))
            XCTAssertEqual(view.alphaValue, annotation.camera.id == cameras[0].id ? 1 : 0.18, accuracy: 0.001)
            let marker = try XCTUnwrap(view as? MKMarkerAnnotationView)
            XCTAssertEqual(marker.titleVisibility, annotation.camera.id == cameras[0].id ? .visible : .hidden)
        }
        let opacities = map.overlays.map { coordinator.mapView(map, rendererFor: $0).alpha }
        XCTAssertEqual(opacities.filter { $0 == 1 }.count, 5)
        XCTAssertEqual(opacities.filter { abs($0 - 0.18) < 0.001 }.count, 5)
    }

    @MainActor
    func testClearedOrFilteredSelectionRestoresCameraVisibility() throws {
        let cameras = try cameras()
        let coordinator = PhotoMapCoordinator(selection: { _ in })
        let map = MKMapView()
        coordinator.update(map: map, cameras: cameras, identity: "project", selected: nil)
        let normalWidth = try XCTUnwrap(map.overlays.compactMap { $0 as? MKPolygon }.first).boundingMapRect.width
        coordinator.update(map: map, cameras: cameras, identity: "project", selected: cameras[0].id)

        for selection in [nil, "filtered-out-photo"] as [String?] {
            coordinator.update(map: map, cameras: cameras, identity: "project", selected: selection)
            for annotation in map.annotations {
                let view = try XCTUnwrap(coordinator.mapView(map, viewFor: annotation))
                XCTAssertEqual(view.alphaValue, 1)
            }
            XCTAssertTrue(map.overlays.allSatisfy { coordinator.mapView(map, rendererFor: $0).alpha == 1 })
            for polygon in map.overlays.compactMap({ $0 as? MKPolygon }) {
                XCTAssertEqual(polygon.boundingMapRect.width, normalWidth, accuracy: 0.01)
            }
        }
    }

    func testMapDisplayScalePreservesTheApexAndOriginalCameraCorners() throws {
        let camera = try XCTUnwrap(cameras().first)
        let enlarged = camera.displayCorners(selected: true)
        for (original, displayed) in zip(camera.corners, enlarged) {
            XCTAssertEqual(
                displayed.latitude - camera.position.latitude,
                (original.latitude - camera.position.latitude) * 8, accuracy: 1e-10)
            XCTAssertEqual(
                displayed.longitude - camera.position.longitude,
                (original.longitude - camera.position.longitude) * 8, accuracy: 1e-10)
        }
        XCTAssertEqual(camera.displayCorners(selected: false).map(\.latitude), camera.corners.map(\.latitude))
    }

    private func cameras() throws -> [MapCamera] {
        try [0, 1].map { index in
            let json = """
                {"id":"camera-\(index)","name":"photo-\(index).jpg","source":"photo.jpg","preview":"preview.jpg",
                 "width":1000,"height":800,"camera":"test","focalPixels":500,"enabled":true,"sets":[],"metadata":{},
                 "gps":{"latitude":-33,"longitude":\(150 + Double(index) * 0.001),"altitude":50},
                 "attitude":{"yaw":0,"pitch":0,"roll":0}}
                """
            let photo = try JSONDecoder().decode(Photo.self, from: Data(json.utf8))
            return try XCTUnwrap(
                MapCameraProjector().cameras(
                    photos: [photo], hints: [], reconstruction: nil, aligned: false, distance: 20
                ).first)
        }
    }
}
