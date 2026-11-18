import Foundation

struct MatchingArtifactReader {
    func read<T>(_ file: URL, metadata: Data) throws -> T {
        if T.self == FeatureSet.self {
            return try features(file, metadata: metadata) as! T
        }
        let header = try JSONDecoder().decode(MatchArtifactHeader.self, from: metadata)
        guard header.storage == "echo-matches-v1", header.pointCount >= 0, header.lineCount >= 0 else {
            throw CLIError(message: "Unsupported matching artifact. Migrate this project to binary storage.")
        }
        if T.self == ImagePairSummary.self {
            return ImagePairSummary(a: header.a, b: header.b, count: header.pointCount) as! T
        }
        let bytes = try length(header.pointCount, 48, header.lineCount, 80)
        let buffer = try MatchingBinaryBuffer(header: file, payload: header.payload, kind: 2, bytes: bytes)
        var matches: [ImageMatch] = []
        var lines: [LineMatch] = []
        matches.reserveCapacity(header.pointCount)
        for index in 0..<header.pointCount {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let row = 16 + index * 48
            let kind = buffer.integer(row + 40)
            let flags = buffer.integer(row + 44)
            guard header.kinds.indices.contains(kind), flags & ~31 == 0, flags & 3 != 3 else {
                throw CLIError(message: "Invalid match kind or flags")
            }
            matches.append(
                ImageMatch(
                    a: flags & 8 == 0 ? [] : buffer.pixel(row), b: flags & 16 == 0 ? [] : buffer.pixel(row + 16),
                    score: buffer.double(row + 32), manual: flags & 4 != 0,
                    geometricInlier: flags & 3 == 0 ? nil : flags & 3 == 2, kind: header.kinds[kind]))
        }
        for index in 0..<header.lineCount {
            let row = 16 + header.pointCount * 48 + index * 80
            lines.append(
                LineMatch(
                    indexA: buffer.integer(row), indexB: buffer.integer(row + 4),
                    startA: buffer.pixel(row + 8), endA: buffer.pixel(row + 24), startB: buffer.pixel(row + 40),
                    endB: buffer.pixel(row + 56), score: buffer.double(row + 72)))
        }
        return ImagePair(
            a: header.a, b: header.b, matches: matches, lineMatches: lines, diagnostics: header.diagnostics) as! T
    }

    private func features(_ file: URL, metadata: Data) throws -> FeatureSet {
        let header = try JSONDecoder().decode(FeatureArtifactHeader.self, from: metadata)
        guard header.storage == "echo-features-v1", header.descriptorCount >= 0 else {
            throw CLIError(message: "Unsupported feature artifact. Migrate this project to binary storage.")
        }
        let tableBytes = try length(header.pointCount, 32, header.lineCount, 48)
        let bytes = try length(tableBytes, 1, header.descriptorCount, 4)
        let buffer = try MatchingBinaryBuffer(header: file, payload: header.payload, kind: 1, bytes: bytes)
        func descriptor(_ row: Int) throws -> FeatureDescriptorSlice {
            let offset = buffer.integer(row)
            let count = buffer.integer(row + 4)
            guard offset >= 0, count >= 0, offset <= header.descriptorCount,
                count <= header.descriptorCount - offset
            else { throw CLIError(message: "Invalid descriptor range") }
            return FeatureDescriptorSlice(data: buffer.data, offset: 16 + tableBytes + offset * 4, count: count)
        }
        var points: [ImageFeature] = []
        var lines: [LineFeature] = []
        points.reserveCapacity(header.pointCount)
        for index in 0..<header.pointCount {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let row = 16 + index * 32
            let kind = buffer.integer(row + 24)
            guard header.kinds.indices.contains(kind) else { throw CLIError(message: "Invalid feature kind") }
            points.append(
                ImageFeature(
                    x: buffer.double(row), y: buffer.double(row + 8), kind: header.kinds[kind],
                    descriptor: [], descriptorSource: try descriptor(row + 16)))
        }
        for index in 0..<header.lineCount {
            let row = 16 + header.pointCount * 32 + index * 48
            lines.append(
                LineFeature(
                    start: buffer.pixel(row), end: buffer.pixel(row + 16), descriptor: [],
                    strength: buffer.double(row + 32), descriptorSource: try descriptor(row + 40)))
        }
        return FeatureSet(imageId: header.imageId, lines: lines, features: points)
    }

    private func length(_ first: Int, _ firstSize: Int, _ second: Int, _ secondSize: Int) throws -> Int {
        guard first >= 0, second >= 0, first <= (Int.max - 16) / firstSize,
            second <= (Int.max - 16 - first * firstSize) / secondSize
        else {
            throw CLIError(message: "Invalid matching binary dimensions")
        }
        return first * firstSize + second * secondSize
    }
}
