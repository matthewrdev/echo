import EchoRendering
import Foundation

struct BulkArtifactReader {
    func read<T>(_ file: URL, metadata: Data) throws -> T {
        let header = try JSONDecoder().decode(BulkArtifactHeader.self, from: metadata)
        guard header.storage == "echo-binary-v1" else { throw CLIError(message: "Unsupported binary artifact version") }
        func buffer(_ name: String, _ size: Int) throws -> BinaryRecordBuffer {
            guard let path = header.buffers[name], !path.isEmpty,
                URL(fileURLWithPath: path).lastPathComponent == path, path != ".", path != ".."
            else { throw CLIError(message: "Invalid binary artifact buffer path") }
            return try BinaryRecordBuffer(
                file: file.deletingLastPathComponent().appendingPathComponent(path), size: size)
        }
        let result: Any
        switch header.kind {
        case "scene":
            guard let scene = header.scene else { throw CLIError(message: "Missing scene metadata") }
            if T.self == ReconstructionMetadata.self {
                result = ReconstructionMetadata(frame: scene.frame, origin: scene.origin, cameras: scene.cameras)
            } else {
                let records = try buffer("points", 76)
                let points = try SparsePointRecordReader().read(records, images: header.images)
                result = ReconstructionScene(
                    frame: scene.frame, origin: scene.origin, cameras: scene.cameras,
                    points: points, unplaced: scene.unplaced, warnings: scene.warnings)
            }
        case "depth":
            let depths = try buffer("depth", 4)
            let confidence = try buffer("confidence", 4)
            guard header.width > 0, header.height > 0,
                header.width <= Int.max / header.height,
                header.width * header.height == depths.count,
                confidence.count == depths.count
            else { throw CLIError(message: "Invalid binary depth dimensions") }
            result = DepthMap(
                imageId: header.imageId, width: header.width, height: header.height,
                depth: (0..<depths.count).map { depths.float($0) },
                confidence: (0..<confidence.count).map { confidence.float($0) })
        case "semantics":
            let sky = try buffer("sky", 1)
            guard header.width > 0, header.height > 0,
                header.width <= Int.max / header.height,
                header.width * header.height == sky.count
            else { throw CLIError(message: "Invalid binary sky-mask dimensions") }
            result = SkyProbabilityMap(
                imageId: header.imageId, width: header.width, height: header.height,
                sky: (0..<sky.count).map { sky.byte($0, 0) })
        case "surface":
            let vertices = try buffer("vertices", 24)
            let faces = try buffer("faces", 12)
            let sources = try buffer("sources", 4)
            var points: [[Double]] = []
            var triangles: [[Int]] = []
            var images: [String] = []
            for index in 0..<vertices.count {
                if index % 4096 == 0 { try Task.checkCancellation() }
                points.append([vertices.double(index), vertices.double(index, 8), vertices.double(index, 16)])
            }
            for index in 0..<faces.count {
                if index % 4096 == 0 { try Task.checkCancellation() }
                let face = [faces.integer(index), faces.integer(index, 4), faces.integer(index, 8)]
                guard face.allSatisfy({ $0 >= 0 && $0 < vertices.count }) else {
                    throw CLIError(message: "Invalid binary triangle index")
                }
                triangles.append(face)
            }
            for index in 0..<sources.count {
                let source = sources.integer(index)
                guard header.images.indices.contains(source) else {
                    throw CLIError(message: "Invalid photograph index")
                }
                images.append(header.images[source])
            }
            result = Surface(vertices: points, faces: triangles, sourceImages: images)
        case "uvs":
            let records = try buffer("uvs", 16)
            var values: [[Double]] = []
            for index in 0..<records.count {
                if index % 4096 == 0 { try Task.checkCancellation() }
                values.append([records.double(index), records.double(index, 8)])
            }
            result = values
        case "volume":
            let samples = try buffer("samples", 20)
            var values: [VolumeSample] = []
            for index in 0..<samples.count {
                if index % 4096 == 0 { try Task.checkCancellation() }
                values.append(
                    VolumeSample(
                        x: samples.integer(index), y: samples.integer(index, 4),
                        z: samples.integer(index, 8), distance: samples.float(index, 12),
                        views: samples.integer(index, 16)))
            }
            result = FusedVolume(voxelSize: header.voxelSize, samples: values)
        default: throw CLIError(message: "Unsupported binary artifact kind")
        }
        guard let typed = result as? T else { throw CLIError(message: "Binary artifact type mismatch") }
        return typed
    }
}
