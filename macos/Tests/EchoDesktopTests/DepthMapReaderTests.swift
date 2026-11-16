import Foundation
import XCTest

@testable import EchoDesktop

final class DepthMapReaderTests: XCTestCase {
    func testBinaryReaderPreservesFloatBitsAcrossExponentRange() throws {
        try withDirectory { directory in
            var bits: UInt32 = 41
            var samples: [Float] = [0, -0.0, .leastNonzeroMagnitude, .leastNormalMagnitude, .greatestFiniteMagnitude]
            for _ in 0..<10_000 {
                bits = bits &* 1_664_525 &+ 1_013_904_223
                let value = Float(bitPattern: bits)
                if value.isFinite { samples.append(value) }
            }
            let file = try save(directory, values: samples)
            let map = try DepthMapReader().read(file)
            XCTAssertEqual(map.imageId, "photo one")
            XCTAssertEqual(map.width, samples.count)
            XCTAssertEqual(map.depth.map(\.bitPattern), samples.map(\.bitPattern))
            XCTAssertEqual(map.confidence.map(\.bitPattern), samples.map(\.bitPattern))
        }
    }

    func testReaderRejectsTruncatedRecordsAndUnsupportedStorage() throws {
        try withDirectory { directory in
            let file = try save(directory, values: [1, 2])
            let buffer = directory.appendingPathComponent("samples.bin")
            let original = try Data(contentsOf: buffer)
            for count in 0..<original.count {
                try original.prefix(count).write(to: buffer)
                XCTAssertThrowsError(try DepthMapReader().read(file))
            }
            try original.write(to: buffer)
            let metadata = try String(contentsOf: file, encoding: .utf8)
            try Data(metadata.replacingOccurrences(of: "echo-binary-v1", with: "echo-binary-v99").utf8).write(to: file)
            XCTAssertThrowsError(try DepthMapReader().read(file))
            try Data(#"{"width":1,"height":1,"depth":[1],"confidence":[1]}"#.utf8).write(to: file)
            XCTAssertThrowsError(try DepthMapReader().read(file))
        }
    }

    func testReaderRejectsDimensionMismatchAndEscapingBufferPaths() throws {
        try withDirectory { directory in
            let file = try save(directory, values: [1, 2])
            let original = try JSONSerialization.jsonObject(with: Data(contentsOf: file)) as! [String: Any]
            var invalid = original
            invalid["width"] = 3
            try JSONSerialization.data(withJSONObject: invalid).write(to: file)
            XCTAssertThrowsError(try DepthMapReader().read(file))
            invalid = original
            invalid["buffers"] = ["depth": "../samples.bin", "confidence": "samples.bin"]
            try JSONSerialization.data(withJSONObject: invalid).write(to: file)
            XCTAssertThrowsError(try DepthMapReader().read(file))
        }
    }

    private func save(_ directory: URL, values: [Float]) throws -> URL {
        var data = Data()
        for value in [UInt32(0x3150_4345), 1, 4, UInt32(values.count)] + values.map(\.bitPattern) {
            var encoded = value.littleEndian
            withUnsafeBytes(of: &encoded) { data.append(contentsOf: $0) }
        }
        try data.write(to: directory.appendingPathComponent("samples.bin"))
        let file = directory.appendingPathComponent("depth.json")
        let header: [String: Any] = [
            "storage": "echo-binary-v1", "kind": "depth", "imageId": "photo one",
            "width": values.count, "height": 1, "voxelSize": 0, "images": [],
            "buffers": ["depth": "samples.bin", "confidence": "samples.bin"],
        ]
        try JSONSerialization.data(withJSONObject: header).write(to: file)
        return file
    }

    private func withDirectory(_ action: (URL) throws -> Void) throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        try action(directory)
    }
}
