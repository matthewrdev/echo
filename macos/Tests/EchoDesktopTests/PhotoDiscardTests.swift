import Foundation
import XCTest

@testable import EchoDesktop

final class PhotoDiscardTests: XCTestCase {
    func testOlderProjectsDecodeWithoutDiscardFields() throws {
        let photo = try decode(extra: "")
        XCTAssertFalse(photo.isDiscarded)
        XCTAssertNil(photo.duplicateOf)
    }

    func testDiscardedPhotosKeepTheirRepresentativeAndDescription() throws {
        let photo = try decode(extra: #", "discarded": true, "duplicateOf": "keeper""#)
        XCTAssertTrue(photo.isDiscarded)
        XCTAssertEqual(photo.duplicateOf, "keeper")
        XCTAssertTrue(photo.hoverSummary.contains("Discarded"))
    }

    private func decode(extra: String) throws -> Photo {
        let json = """
            {"id":"photo","name":"photo.jpg","source":"source.jpg","preview":"preview.jpg",
             "width":320,"height":180,"camera":"DJI","focalPixels":200,"enabled":false,
             "sets":[],"metadata":{}\(extra)}
            """
        return try JSONDecoder().decode(Photo.self, from: Data(json.utf8))
    }
}
