import Foundation
import XCTest

@testable import EchoDesktop

final class DepthMapReaderTests: XCTestCase {
    func testBufferReaderPreservesSavedFloatSamples() throws {
        let json =
            #"{"imageId":"photo\u0020one","width":4,"height":1,"depth":[0,12.345678,1.25e-3,3.4028235e38],"confidence":[0,0.123456789,1,5e-1]}"#
        let expected = try JSONDecoder().decode(DepthMap.self, from: Data(json.utf8))
        let map = try read(json)
        XCTAssertEqual(map.imageId, expected.imageId)
        XCTAssertEqual(map.width, expected.width)
        XCTAssertEqual(map.height, expected.height)
        XCTAssertEqual(map.depth.map(\.bitPattern), expected.depth.map(\.bitPattern))
        XCTAssertEqual(map.confidence.map(\.bitPattern), expected.confidence.map(\.bitPattern))
    }

    func testBufferReaderRejectsMalformedDimensionsAndSamples() {
        for json in [
            #"{"imageId":"p","width":true,"height":1,"depth":[1],"confidence":[1]}"#,
            #"{"imageId":"p","width":1.5,"height":1,"depth":[1],"confidence":[1]}"#,
            #"{"imageId":"p","width":-1,"height":1,"depth":[],"confidence":[]}"#,
            #"{"imageId":"p","width":9223372036854775807,"height":2,"depth":[],"confidence":[]}"#,
            #"{"imageId":"p","width":2,"height":1,"depth":[1],"confidence":[1]}"#,
            #"{"imageId":"p","width":1,"height":1,"depth":[1],"confidence":[]}"#,
            #"{"imageId":"p","width":1,"height":1,"depth":[true],"confidence":[1]}"#,
            #"{"imageId":"p","width":1,"height":1,"depth":[1],"confidence":[false]}"#,
            #"{"imageId":"p","width":1,"height":1,"depth":["1"],"confidence":[1]}"#,
            #"{"imageId":"p","width":1,"height":1,"depth":[null],"confidence":[1]}"#,
            #"{"imageId":"p","width":1,"height":1,"depth":[1e40],"confidence":[1]}"#,
        ] {
            XCTAssertThrowsError(try read(json), json)
        }
    }

    func testBufferParserPreservesFloatRoundingAcrossExponentRange() throws {
        var bits: UInt32 = 41
        var samples: [Float] = [0, -0.0, .leastNonzeroMagnitude, .leastNormalMagnitude, .greatestFiniteMagnitude]
        for _ in 0..<10_000 {
            bits = bits &* 1_664_525 &+ 1_013_904_223
            let value = Float(bitPattern: bits)
            if value.isFinite { samples.append(value) }
        }
        let array = String(decoding: try JSONEncoder().encode(samples), as: UTF8.self)
        let json =
            "{\"imageId\":\"p\",\"width\":\(samples.count),\"height\":1,\"depth\":\(array),\"confidence\":\(array)}"
        let expected = try JSONDecoder().decode(DepthMap.self, from: Data(json.utf8))
        let actual = try read(json)
        XCTAssertEqual(actual.depth.map(\.bitPattern), expected.depth.map(\.bitPattern))
        XCTAssertEqual(actual.confidence.map(\.bitPattern), expected.confidence.map(\.bitPattern))
    }

    func testFieldOrderEscapesWhitespaceAndUnknownFields() throws {
        let json =
            #" {"extra":{"list":[null,true,false,{"escaped":"\"\\\u0041"}]}, "confidence": [0.5, 1e-2], "depth": [2.5, -1], "height": 1, "width": 2, "imageId": "photo\u0020one"} "#
        let map = try read(json)
        XCTAssertEqual(map.imageId, "photo one")
        XCTAssertEqual(map.depth, [2.5, -1])
        XCTAssertEqual(map.confidence, [0.5, 0.01])
    }

    func testTruncatedAndInvalidJSONCannotReadPastTheBuffer() throws {
        let json = #"{"imageId":"p","width":1,"height":1,"depth":[12.5e-2],"confidence":[0.9]}"#
        for length in 0..<json.utf8.count {
            XCTAssertThrowsError(try read(String(json.prefix(length))), "Truncated at \(length)")
        }
        for number in ["01", "+1", "1.", ".5", "1e", "1e+", "--1", "NaN", "Infinity", "0x123", "1_000", "1 2"] {
            XCTAssertThrowsError(try read(json.replacingOccurrences(of: "12.5e-2", with: number)), number)
        }
        for invalid in [
            json + " trailing", json.replacingOccurrences(of: "0.9]", with: "0.9,]"),
            json.replacingOccurrences(of: "\"height\":1", with: "\"width\":1"),
            json.replacingOccurrences(of: "\"p\"", with: "\"\\uZZZZ\""),
            json.replacingOccurrences(of: "\"p\"", with: "\"line\nbreak\""),
        ] {
            XCTAssertThrowsError(try read(invalid))
        }
    }

    private func read(_ json: String) throws -> DepthMap {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".json")
        defer { try? FileManager.default.removeItem(at: file) }
        try Data(json.utf8).write(to: file)
        return try DepthMapReader().read(file)
    }
}
