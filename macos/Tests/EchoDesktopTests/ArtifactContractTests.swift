import Foundation
import XCTest

@testable import EchoDesktop

final class ArtifactContractTests: XCTestCase {
    func testPointColorsUseTheDotNetByteArrayEncoding() throws {
        let json = Data(#"{"position":[1,2,3],"colour":"/4AA","error":0.25}"#.utf8)
        let point = try JSONDecoder().decode(CloudPoint.self, from: json)
        XCTAssertEqual(point.position, [1, 2, 3])
        XCTAssertEqual(point.colour, Data([255, 128, 0]))
        XCTAssertEqual(point.error, 0.25)
    }

    func testSparsePointsMayHaveNoColor() throws {
        let json = Data(#"{"position":[1,2,3],"colour":null,"error":0.5}"#.utf8)
        let point = try JSONDecoder().decode(CloudPoint.self, from: json)
        XCTAssertNil(point.colour)
    }
}
