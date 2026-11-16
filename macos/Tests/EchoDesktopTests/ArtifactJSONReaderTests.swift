import Foundation
import XCTest

@testable import EchoDesktop

final class ArtifactJSONReaderTests: XCTestCase {
    func testBulkContractsMatchDecodableIncludingOptionalLegacyFields() throws {
        try compare(
            ReconstructionScene.self,
            #"{"frame":"local metres","origin":{"latitude":1,"longitude":2},"cameras":[{"imageId":"p","centre":[1,2,3],"rotation":[1,0,0,0,1,0,0,0,1],"focalPixels":100,"source":"saved"}],"points":[{"position":[1.5,-2,3e-5],"colour":"AQID","error":0.2,"imageA":"p","imageB":"q","pixelA":[0,1],"pixelB":[2,3]},{"position":[0,0,0],"error":0}],"unplaced":["r"],"warnings":["hello\u0020world"]}"#
        )
        try compare(Surface.self, #"{"faces":[[0,1,2]],"vertices":[[0,0,0],[1,0,0],[0,1,0]],"sourceImages":["p"]}"#)
        try compare(
            TextureArtifact.self,
            #"{"uvsFile":"uvs.json","uvs":[],"size":256,"meshFile":"mesh.json","chartCount":3,"coverageFraction":0.8}"#)
        try compare(TextureArtifact.self, #"{"uvsFile":"uvs.json","uvs":[],"size":256}"#)
        try compare(
            FeatureSet.self,
            #"{"imageId":"p","features":[{"x":1,"y":2,"descriptor":[0,0.125,1e-5],"kind":"corner"},{"x":3,"y":4,"descriptor":[]}],"lines":[{"start":[1,2],"end":[3,4],"descriptor":[0.5],"strength":1}],"future":{"items":[null,true,false,{"a":"\"\\"}]}}"#
        )
        try compare(FeatureSet.self, #"{"imageId":"p","features":[]}"#)
        try compare(
            ImagePair.self,
            #"{"a":"p","b":"q","matches":[{"a":[1,2],"b":[3,4],"score":0.5,"manual":false,"geometricInlier":true,"kind":"line","lineA":[[1,2],[3,4]],"lineB":null},{"a":[0,0],"b":[1,1],"manual":true}],"lineMatches":[{"indexA":0,"indexB":1,"startA":[1,2],"endA":[3,4],"startB":[5,6],"endB":[7,8],"score":0.9}],"diagnostics":{"featuresA":2,"featuresB":3,"accepted":1,"rejectedNonMutual":0,"rejectedAmbiguous":0,"rejectedDistance":1,"ratioThreshold":0.72,"distanceThreshold":0.65}}"#
        )
        try compare(
            DenseCloudReport.self,
            #"{"inputSamples":10,"retainedSamples":1,"preview":[{"position":[0,1,2],"error":0.3}]}"#)
        try compare(
            FusedVolume.self, #"{"voxelSize":0.01,"samples":[{"x":-1,"y":2,"z":3,"distance":1.25e-3,"views":4}]}"#)
    }

    func testNumbersAndEscapedNamesPreserveValues() throws {
        var bits: UInt64 = 41
        var samples: [Double] = [0, -0.0, .leastNonzeroMagnitude, .leastNormalMagnitude, .greatestFiniteMagnitude]
        for _ in 0..<10_000 {
            bits = bits &* 6_364_136_223_846_793_005 &+ 1
            let value = Double(bitPattern: bits)
            if value.isFinite { samples.append(value) }
        }
        let array = String(decoding: try JSONEncoder().encode(samples), as: UTF8.self)
        let json = Data("{\"x\":0,\"y\":0,\"descr\\u0069ptor\":\(array)}".utf8)
        let actual: ImageFeature = try ArtifactJSONReader().decode(json)
        let expected = try JSONDecoder().decode(ImageFeature.self, from: json)
        XCTAssertEqual(actual.descriptor.map(\.bitPattern), expected.descriptor.map(\.bitPattern))
        try compare(VolumeSample.self, "{\"x\":\(Int.min),\"y\":\(Int.max),\"z\":1e2,\"distance\":0.1,\"views\":4.0}")
    }

    func testTruncatedMalformedAndDeeplyNestedInputsFailSafely() {
        let json = #"{"imageId":"p","features":[{"x":1.25e-3,"y":2,"descriptor":[1]}]}"#
        let reader = ArtifactJSONReader()
        for length in 0..<json.utf8.count {
            XCTAssertThrowsError(try reader.decode(Data(json.utf8.prefix(length))) as FeatureSet)
        }
        for number in ["01", "+1", "1.", ".5", "1e", "1e+", "--1", "NaN", "Infinity", "0x123", "1_000", "1 2", "1e999"]
        {
            XCTAssertThrowsError(
                try reader.decode(Data(json.replacingOccurrences(of: "1.25e-3", with: number).utf8)) as FeatureSet,
                number)
        }
        for invalid in [
            json + " trailing", json.replacingOccurrences(of: "[1]", with: "[1,]"),
            json.replacingOccurrences(of: "\"imageId\":\"p\",", with: ""),
            json.replacingOccurrences(of: "\"p\"", with: "\"\\uZZZZ\""),
            json.replacingOccurrences(of: "\"p\"", with: "\"line\nbreak\""),
            "{\"extra\":" + String(repeating: "[", count: 200) + "0" + String(repeating: "]", count: 200) + "}",
        ] {
            XCTAssertThrowsError(try reader.decode(Data(invalid.utf8)) as FeatureSet)
        }
    }

    func testSummarySkipsPayloadAndCacheInvalidatesChangedFiles() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".json")
        defer { try? FileManager.default.removeItem(at: file) }
        let json = ArtifactJSONReader()
        try Data(#"{"a":"p","b":"q","matches":[{"unused":[1,2,3]},null],"lineMatches":[]}"#.utf8).write(to: file)
        let first: ImagePairSummary = try json.read(file)
        XCTAssertEqual(first.a, "p")
        XCTAssertEqual(first.count, 2)
        XCTAssertThrowsError(try json.read(file) as ImagePair)
        try Data(#"{"a":"replacement","b":"q","matches":[]}"#.utf8).write(to: file, options: .atomic)
        let second: ImagePairSummary = try json.read(file)
        XCTAssertEqual(second.a, "replacement")
        XCTAssertEqual(second.count, 0)
        try FileManager.default.removeItem(at: file)
        XCTAssertThrowsError(try json.read(file) as ImagePairSummary)
        let metadata: ReconstructionMetadata = try json.decode(
            Data(#"{"frame":"arbitrary","cameras":[],"points":[{"unused":[1,2,3]}]}"#.utf8))
        XCTAssertEqual(metadata.cameras.count, 0)
    }

    func testSavedProjectBulkDecoderParityAndTimings() throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_PROJECT"], let id = environment["ECHO_TEST_RUN"] else {
            throw XCTSkip("Saved reconstruction fixture unavailable")
        }
        let root = URL(fileURLWithPath: path)
        let json = ArtifactJSONReader()
        let run: RunData = try json.read(root.appendingPathComponent("runs/\(id)/run.json"))
        let sceneFile = root.appendingPathComponent(try XCTUnwrap(run.artifacts["alignment"])).appendingPathComponent(
            "scene.json")
        let bytes = try Data(contentsOf: sceneFile)
        let legacyStart = Date()
        let expected = try JSONDecoder().decode(ReconstructionScene.self, from: bytes)
        let fastStart = Date()
        let actual: ReconstructionScene = try json.read(sceneFile)
        let cachedStart = Date()
        let cached: ReconstructionScene = try json.read(sceneFile)
        print(
            "SCENE DECODE legacy \(fastStart.timeIntervalSince(legacyStart)) s, direct \(cachedStart.timeIntervalSince(fastStart)) s, cached \(Date().timeIntervalSince(cachedStart)) s"
        )
        XCTAssertEqual(actual.points.map(\.position), expected.points.map(\.position))
        XCTAssertEqual(actual.points.map(\.pixelA), expected.points.map(\.pixelA))
        XCTAssertEqual(actual.points.map(\.colour), expected.points.map(\.colour))
        XCTAssertEqual(cached.points.count, expected.points.count)
        let featuresFolder = root.appendingPathComponent(try XCTUnwrap(run.artifacts["features"]))
        let units: [String: String] = try json.read(featuresFolder.appendingPathComponent("units.json"))
        let file = root.appendingPathComponent(try XCTUnwrap(units.sorted { $0.key < $1.key }.first?.value))
            .appendingPathComponent("features.json")
        let featureBytes = try Data(contentsOf: file)
        let beforeLegacy = Date()
        let oldFeatures = try JSONDecoder().decode(FeatureSet.self, from: featureBytes)
        let beforeFast = Date()
        let features: FeatureSet = try json.read(file)
        print(
            "FEATURE DECODE legacy \(beforeFast.timeIntervalSince(beforeLegacy)) s, direct \(Date().timeIntervalSince(beforeFast)) s"
        )
        XCTAssertEqual(features.features.map(\.descriptor), oldFeatures.features.map(\.descriptor))
        XCTAssertEqual(features.features.map(\.x), oldFeatures.features.map(\.x))
        let indexer = FeatureMatchIndexer(json: json)
        let firstStart = Date()
        let first = try indexer.read(root: root, run: run, features: features)
        let secondStart = Date()
        let second = try indexer.read(root: root, run: run, features: features)
        print(
            "FEATURE LINKS first \(secondStart.timeIntervalSince(firstStart)) s, cached \(Date().timeIntervalSince(secondStart)) s; \(first.count) links"
        )
        XCTAssertEqual(first.count, second.count)
    }

    private func compare<T: Decodable>(_ type: T.Type, _ json: String) throws {
        let bytes = Data(json.utf8)
        let expected = try JSONDecoder().decode(type, from: bytes)
        let actual: T = try ArtifactJSONReader().decode(bytes)
        XCTAssertEqual(String(reflecting: actual), String(reflecting: expected))
    }
}
