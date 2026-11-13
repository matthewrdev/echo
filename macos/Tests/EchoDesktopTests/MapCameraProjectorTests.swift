import Foundation
import XCTest

@testable import EchoDesktop

final class MapCameraProjectorTests: XCTestCase {
    private func photograph(attitude: String) throws -> Photo {
        let json = """
            {"id":"camera","name":"photo.jpg","source":"photo.jpg","preview":"preview.jpg",
             "width":1000,"height":800,"camera":"test","focalPixels":500,"enabled":true,"sets":[],"metadata":{},
             "gps":{"latitude":-33,"longitude":150,"altitude":50},"attitude":\(attitude)}
            """
        return try JSONDecoder().decode(Photo.self, from: Data(json.utf8))
    }

    func testNorthFacingFrustumUsesFocalLengthAndCameraHeading() throws {
        let photo = try photograph(attitude: #"{"yaw":0,"pitch":0,"roll":0}"#)
        let camera = try XCTUnwrap(
            MapCameraProjector().cameras(
                photos: [photo], hints: [], reconstruction: nil,
                aligned: false, distance: 20
            ).first)
        XCTAssertEqual(camera.corners.count, 4)
        XCTAssertTrue(camera.corners.allSatisfy { $0.latitude > camera.position.latitude })
        XCTAssertLessThan(camera.corners[0].longitude, camera.position.longitude)
        XCTAssertGreaterThan(camera.corners[1].longitude, camera.position.longitude)
    }

    func testMissingOrientationDoesNotInventAFrustum() throws {
        let photo = try photograph(attitude: "null")
        let camera = try XCTUnwrap(
            MapCameraProjector().cameras(
                photos: [photo], hints: [], reconstruction: nil,
                aligned: false, distance: 20
            ).first)
        XCTAssertTrue(camera.corners.isEmpty)
    }

    func testTiltComesFromCameraForwardDirection() throws {
        let photo = try photograph(attitude: #"{"yaw":60,"pitch":-45,"roll":12}"#)
        let camera = try XCTUnwrap(
            MapCameraProjector().cameras(
                photos: [photo], hints: [], reconstruction: nil,
                aligned: false, distance: 20
            ).first)
        XCTAssertEqual(try XCTUnwrap(camera.pitchDegrees), -45, accuracy: 0.001)
    }

    func testCameraHintChangesTheDisplayedFrustum() throws {
        let photo = try photograph(attitude: #"{"yaw":0,"pitch":0,"roll":0}"#)
        let hint = ImageHint(type: "camera", imageId: photo.id, attitude: Attitude(yaw: 90, pitch: 0, roll: 0))
        let camera = try XCTUnwrap(
            MapCameraProjector().cameras(
                photos: [photo], hints: [hint], reconstruction: nil,
                aligned: false, distance: 20
            ).first)
        XCTAssertTrue(camera.corners.allSatisfy { $0.longitude > camera.position.longitude })
    }
}
