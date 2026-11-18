import Foundation

@testable import EchoDesktop

struct MatchingArtifactFixture {
    func writePair(root: URL, artifact: String, a: String, b: String) throws {
        let folder = root.appendingPathComponent(artifact)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        var data = Data()
        for value: UInt32 in [0x3142_4d45, 1, 2, 0] { append(value, to: &data) }
        for value: Double in [20, 20, 30, 30, 0.25] { append(value.bitPattern, to: &data) }
        append(UInt32(0), to: &data)
        append(UInt32(26), to: &data)
        try data.write(to: folder.appendingPathComponent("pair.bin"))
        try JSONSerialization.data(withJSONObject: [
            "storage": "echo-matches-v1", "a": a, "b": b, "payload": "pair.bin",
            "pointCount": 1, "lineCount": 0, "kinds": ["corner"],
        ]).write(to: folder.appendingPathComponent("pair.json"))
    }

    private func append<T: FixedWidthInteger>(_ value: T, to data: inout Data) {
        var value = value.littleEndian
        withUnsafeBytes(of: &value) { data.append(contentsOf: $0) }
    }
}
