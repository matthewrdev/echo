import EchoRendering
import Foundation

struct AlignmentPreviewReader {
    func read(_ data: Data) throws -> AlignmentPreview {
        guard data.count >= 8 else { throw CLIError(message: "Truncated sparse preview") }
        let prefix = data.withUnsafeBytes { raw in
            (0..<2).map { Int(Int32(littleEndian: raw.loadUnaligned(fromByteOffset: $0 * 4, as: Int32.self))) }
        }
        guard prefix[0] == 0x3150_5345, prefix[1] >= 0, prefix[1] <= data.count - 8 else {
            throw CLIError(message: "Invalid sparse preview header")
        }
        let end = 8 + prefix[1]
        let header = try JSONDecoder().decode(AlignmentPreviewHeader.self, from: data.subdata(in: 8..<end))
        let records = try BinaryRecordBuffer(data: data, size: 76, headerOffset: end)
        guard header.totalPoints >= records.count else { throw CLIError(message: "Invalid sparse preview count") }
        let points = try SparsePointRecordReader().read(records, images: header.images)
        let scene = header.scene
        return AlignmentPreview(
            scene: ReconstructionScene(
                frame: scene.frame, origin: scene.origin, cameras: scene.cameras, points: points,
                unplaced: scene.unplaced, warnings: scene.warnings),
            totalPoints: header.totalPoints)
    }
}
