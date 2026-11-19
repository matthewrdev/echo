import Foundation

struct SparsePointRecordReader {
    func read(_ records: BinaryRecordBuffer, images: [String]) throws -> [CloudPoint] {
        var points: [CloudPoint] = []
        points.reserveCapacity(records.count)
        for index in 0..<records.count {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let a = records.integer(index, 64)
            let b = records.integer(index, 68)
            let flags = records.byte(index, 72)
            guard images.indices.contains(a), images.indices.contains(b), flags & ~15 == 0 else {
                throw CLIError(message: "Invalid sparse point record")
            }
            points.append(
                CloudPoint(
                    position: flags & 2 == 0
                        ? [] : [records.double(index), records.double(index, 8), records.double(index, 16)],
                    colour: flags & 1 == 0
                        ? nil : Data([records.byte(index, 73), records.byte(index, 74), records.byte(index, 75)]),
                    error: records.double(index, 56), imageA: images[a], imageB: images[b],
                    pixelA: flags & 4 == 0 ? [] : [records.double(index, 24), records.double(index, 32)],
                    pixelB: flags & 8 == 0 ? [] : [records.double(index, 40), records.double(index, 48)]))
        }
        return points
    }
}
