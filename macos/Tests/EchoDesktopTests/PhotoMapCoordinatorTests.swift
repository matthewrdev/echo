import MapKit
import XCTest

@testable import EchoDesktop

final class PhotoMapCoordinatorTests: XCTestCase {
    @MainActor
    func testSelectionKeepsCameraGeometryAndFadesOtherCameras() throws {
        let cameras = try cameras()
        let coordinator = PhotoMapCoordinator(selection: { _ in })
        let map = MKMapView()
        coordinator.update(map: map, cameras: cameras, identity: "project", selected: nil)
        let annotations = map.annotations.compactMap { $0 as? PhotoMapAnnotation }
        let overlays = map.overlays

        coordinator.update(map: map, cameras: cameras, identity: "project", selected: cameras[0].id)

        XCTAssertEqual(map.annotations.count, 2)
        XCTAssertEqual(map.overlays.count, 10)
        XCTAssertTrue(annotations.allSatisfy { original in map.annotations.contains { $0 === original } })
        XCTAssertTrue(overlays.allSatisfy { original in map.overlays.contains { $0 === original } })
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
        coordinator.update(map: map, cameras: cameras, identity: "project", selected: cameras[0].id)

        for selection in [nil, "filtered-out-photo"] as [String?] {
            coordinator.update(map: map, cameras: cameras, identity: "project", selected: selection)
            for annotation in map.annotations {
                let view = try XCTUnwrap(coordinator.mapView(map, viewFor: annotation))
                XCTAssertEqual(view.alphaValue, 1)
            }
            XCTAssertTrue(map.overlays.allSatisfy { coordinator.mapView(map, rendererFor: $0).alpha == 1 })
        }
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
