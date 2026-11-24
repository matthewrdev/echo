import Foundation

struct PackedPreviewReader {
    func cloud(_ bytes: Data, includePreview: Bool = true) throws -> DenseCloudReport {
        var reader = BinaryJournalCursor(bytes)
        guard try reader.integer() == 0x3144_4345 else { throw malformed }
        let input = Int(try reader.integer())
        let retained = Int(try reader.integer())
        let images = Int(try reader.integer())
        guard images <= bytes.count - reader.offset else { throw malformed }
        var imageIDs: [String] = []
        for _ in 0..<images { imageIDs.append(try reader.string()) }
        let count = Int(try reader.integer())
        guard bytes.count - reader.offset == count * 40 else { throw malformed }
        var points: [CloudPoint] = []
        if includePreview {
            points.reserveCapacity(count)
            for _ in 0..<count {
                try Task.checkCancellation()
                let image = Int(Int32(bitPattern: try reader.integer()))
                guard image >= -1, image < imageIDs.count else { throw malformed }
                let x = Double(bitPattern: try reader.wideInteger())
                let y = Double(bitPattern: try reader.wideInteger())
                let z = Double(bitPattern: try reader.wideInteger())
                let error = Double(bitPattern: try reader.wideInteger())
                let hasColour = try reader.byte()
                let red = try reader.byte()
                let green = try reader.byte()
                let blue = try reader.byte()
                let colour = hasColour == 0 ? nil : Data([red, green, blue])
                points.append(
                    CloudPoint(
                        position: [x, y, z], colour: colour, error: error, imageA: image < 0 ? nil : imageIDs[image]))
            }
        }
        return DenseCloudReport(inputSamples: input, retainedSamples: retained, preview: points)
    }

    func fusion(_ bytes: Data) throws -> FusionPreview {
        var reader = BinaryJournalCursor(bytes)
        guard try reader.integer() == 0x3150_4645 else { throw malformed }
        let total = Int(try reader.integer())
        let voxel = Double(bitPattern: try reader.wideInteger())
        _ = try reader.integer()
        _ = try reader.integer()
        let count = Int(try reader.integer())
        guard bytes.count - reader.offset == count * 20 else { throw malformed }
        var samples: [VolumeSample] = []
        samples.reserveCapacity(count)
        for _ in 0..<count {
            try Task.checkCancellation()
            samples.append(
                try VolumeSample(
                    x: Int(Int32(bitPattern: reader.integer())), y: Int(Int32(bitPattern: reader.integer())),
                    z: Int(Int32(bitPattern: reader.integer())), distance: Float(bitPattern: reader.integer()),
                    views: Int(reader.integer())))
        }
        return FusionPreview(volume: FusedVolume(voxelSize: voxel, samples: samples), totalSamples: total)
    }

    private var malformed: CLIError { CLIError(message: "Invalid packed preview.") }
}
