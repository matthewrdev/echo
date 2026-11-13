import XCTest

@testable import EchoDesktop

final class MetadataEntryTests: XCTestCase {
    func testPrivateDJIPayloadIsDistinguishedFromReadableCameraData() {
        let payload = MetadataEntry(key: "DJI:DJI_0x1002", value: "Undecoded private data")
        XCTAssertTrue(payload.isOpaque)
        XCTAssertTrue(payload.summary.contains("Undecoded vendor payload"))
        let yaw = MetadataEntry(key: "XMP-drone-dji:GimbalYawDegree", value: "-74.3")
        XCTAssertFalse(yaw.isOpaque)
        XCTAssertEqual(yaw.group, "XMP-drone-dji")
        XCTAssertEqual(yaw.name, "GimbalYawDegree")
        XCTAssertEqual(yaw.summary, "-74.3")
    }

    func testBinaryFieldRetainsRawValueButHasReadableSummary() {
        let raw = "(Binary data 1024 bytes, use -b option to extract)"
        let entry = MetadataEntry(key: "DJI:Histogram", value: raw)
        XCTAssertTrue(entry.isBinary)
        XCTAssertEqual(entry.summary, "(Binary data 1024 bytes)")
        XCTAssertEqual(entry.value, raw)
    }
}
