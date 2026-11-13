import Foundation
import XCTest

@testable import EchoDesktop

final class MetadataSampleTests: XCTestCase {
    func testByteOrderUnalignedOffsetsAndTruncatedSamples() {
        let data = Data([99, 1, 2, 3, 4, 255])
        XCTAssertEqual(MetadataSampleFormat.uint16LE.samples(data, byteOffset: 1), [513, 1027])
        XCTAssertEqual(MetadataSampleFormat.uint16BE.samples(data, byteOffset: 1), [258, 772])
        XCTAssertEqual(MetadataSampleFormat.uint32BE.samples(data, byteOffset: 1), [16_909_060])
        XCTAssertEqual(MetadataSampleFormat.byte.samples(data, byteOffset: 5), [255])
        XCTAssertTrue(MetadataSampleFormat.uint16LE.samples(data, byteOffset: 5).isEmpty)
        XCTAssertTrue(MetadataSampleFormat.byte.samples(data, byteOffset: -1).isEmpty)
        XCTAssertTrue(MetadataSampleFormat.byte.samples(data, byteOffset: 6).isEmpty)
    }

    func testFloatingPointInterpretationPreservesNonFiniteValues() {
        XCTAssertEqual(MetadataSampleFormat.float32LE.samples(Data([0, 0, 128, 63]), byteOffset: 0), [1])
        XCTAssertEqual(MetadataSampleFormat.float32BE.samples(Data([63, 128, 0, 0]), byteOffset: 0), [1])
        XCTAssertTrue(MetadataSampleFormat.float32BE.samples(Data([127, 192, 0, 0]), byteOffset: 0)[0].isNaN)
    }
}
