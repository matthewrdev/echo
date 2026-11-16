import Foundation

// Explicit bulk contracts avoid a keyed Decodable container for every numeric sample.
enum ArtifactJSONParser {
    static func reconstructionScene(_ reader: inout UTF8JSONReader) throws -> ReconstructionScene {
        var frame: String?
        var origin: GeoPosition?
        var cameras: [CameraPose]?
        var points: [CloudPoint]?
        var unplaced: [String]?
        var warnings: [String]?
        try reader.object { key, reader in
            switch key {
            case "frame": frame = try reader.string()
            case "origin": origin = try reader.optional { reader in try reader.decode(GeoPosition.self) }
            case "cameras": cameras = try reader.array { reader in try reader.decode(CameraPose.self) }
            case "points": points = try reader.array { reader in try cloudPoint(&reader) }
            case "unplaced": unplaced = try reader.array { reader in try reader.string() }
            case "warnings": warnings = try reader.array { reader in try reader.string() }
            default: try reader.skipValue()
            }
        }
        guard let frame, let cameras, let points, let unplaced, let warnings else { throw reader.invalid() }
        return ReconstructionScene(
            frame: frame, origin: origin, cameras: cameras, points: points, unplaced: unplaced, warnings: warnings)
    }

    static func reconstructionMetadata(_ reader: inout UTF8JSONReader) throws -> ReconstructionMetadata {
        var frame: String?
        var origin: GeoPosition?
        var cameras: [CameraPose]?
        try reader.object { key, reader in
            switch key {
            case "frame": frame = try reader.string()
            case "origin": origin = try reader.optional { reader in try reader.decode(GeoPosition.self) }
            case "cameras": cameras = try reader.array { reader in try reader.decode(CameraPose.self) }
            default: try reader.skipValue()
            }
        }
        guard let frame, let cameras else { throw reader.invalid() }
        return ReconstructionMetadata(frame: frame, origin: origin, cameras: cameras)
    }

    static func cloudPoint(_ reader: inout UTF8JSONReader) throws -> CloudPoint {
        var position: [Double]?
        var colour: Data?
        var error: Double?
        var imageA: String?
        var imageB: String?
        var pixelA: [Double]?
        var pixelB: [Double]?
        try reader.object { key, reader in
            switch key {
            case "position": position = try reader.array { reader in try reader.double() }
            case "colour": colour = try reader.optional { reader in try data(&reader) }
            case "error": error = try reader.double()
            case "imageA": imageA = try reader.optional { reader in try reader.string() }
            case "imageB": imageB = try reader.optional { reader in try reader.string() }
            case "pixelA": pixelA = try reader.optional { reader in try reader.array { reader in try reader.double() } }
            case "pixelB": pixelB = try reader.optional { reader in try reader.array { reader in try reader.double() } }
            default: try reader.skipValue()
            }
        }
        guard let position, let error else { throw reader.invalid() }
        return CloudPoint(
            position: position, colour: colour, error: error, imageA: imageA, imageB: imageB, pixelA: pixelA,
            pixelB: pixelB)
    }

    static func surface(_ reader: inout UTF8JSONReader) throws -> Surface {
        var vertices: [[Double]]?
        var faces: [[Int]]?
        var sourceImages: [String]?
        try reader.object { key, reader in
            switch key {
            case "vertices":
                vertices = try reader.array { reader in try reader.array { reader in try reader.double() } }
            case "faces": faces = try reader.array { reader in try reader.array { reader in try reader.integer() } }
            case "sourceImages": sourceImages = try reader.array { reader in try reader.string() }
            default: try reader.skipValue()
            }
        }
        guard let vertices, let faces, let sourceImages else { throw reader.invalid() }
        return Surface(vertices: vertices, faces: faces, sourceImages: sourceImages)
    }

    static func textureArtifact(_ reader: inout UTF8JSONReader) throws -> TextureArtifact {
        var uvsFile: String?
        var size: Int?
        var meshFile: String?
        var chartCount: Int?
        var coverageFraction: Double?
        try reader.object { key, reader in
            switch key {
            case "uvsFile": uvsFile = try reader.optional { reader in try reader.string() }
            case "size": size = try reader.integer()
            case "meshFile": meshFile = try reader.optional { reader in try reader.string() }
            case "chartCount": chartCount = try reader.optional { reader in try reader.integer() }
            case "coverageFraction": coverageFraction = try reader.optional { reader in try reader.double() }
            default: try reader.skipValue()
            }
        }
        guard uvsFile != nil, let size else { throw reader.invalid() }
        return TextureArtifact(
            uvsFile: uvsFile, uvs: [], size: size, meshFile: meshFile, chartCount: chartCount,
            coverageFraction: coverageFraction)
    }

    static func featureSet(_ reader: inout UTF8JSONReader) throws -> FeatureSet {
        var imageId: String?
        var lines: [LineFeature]?
        var features: [ImageFeature]?
        try reader.object { key, reader in
            switch key {
            case "imageId": imageId = try reader.string()
            case "lines":
                lines = try reader.optional { reader in try reader.array { reader in try lineFeature(&reader) } }
            case "features": features = try reader.array { reader in try imageFeature(&reader) }
            default: try reader.skipValue()
            }
        }
        guard let imageId, let features else { throw reader.invalid() }
        return FeatureSet(imageId: imageId, lines: lines, features: features)
    }

    static func imageFeature(_ reader: inout UTF8JSONReader) throws -> ImageFeature {
        var x: Double?
        var y: Double?
        var kind: String?
        var descriptor: [Double]?
        try reader.object { key, reader in
            switch key {
            case "x": x = try reader.double()
            case "y": y = try reader.double()
            case "kind": kind = try reader.optional { reader in try reader.string() }
            case "descriptor": descriptor = try reader.array { reader in try reader.double() }
            default: try reader.skipValue()
            }
        }
        guard let x, let y, let descriptor else { throw reader.invalid() }
        return ImageFeature(x: x, y: y, kind: kind, descriptor: descriptor)
    }

    static func lineFeature(_ reader: inout UTF8JSONReader) throws -> LineFeature {
        var start: [Double]?
        var end: [Double]?
        var descriptor: [Double]?
        var strength: Double?
        try reader.object { key, reader in
            switch key {
            case "start": start = try reader.array { reader in try reader.double() }
            case "end": end = try reader.array { reader in try reader.double() }
            case "descriptor": descriptor = try reader.array { reader in try reader.double() }
            case "strength": strength = try reader.double()
            default: try reader.skipValue()
            }
        }
        guard let start, let end, let descriptor, let strength else { throw reader.invalid() }
        return LineFeature(start: start, end: end, descriptor: descriptor, strength: strength)
    }

    static func imagePair(_ reader: inout UTF8JSONReader) throws -> ImagePair {
        var a: String?
        var b: String?
        var matches: [ImageMatch]?
        var lineMatches: [LineMatch]?
        var diagnostics: MatchingDiagnostics?
        try reader.object { key, reader in
            switch key {
            case "a": a = try reader.string()
            case "b": b = try reader.string()
            case "matches": matches = try reader.array { reader in try imageMatch(&reader) }
            case "lineMatches":
                lineMatches = try reader.optional { reader in try reader.array { reader in try lineMatch(&reader) } }
            case "diagnostics":
                diagnostics = try reader.optional { reader in try reader.decode(MatchingDiagnostics.self) }
            default: try reader.skipValue()
            }
        }
        guard let a, let b, let matches else { throw reader.invalid() }
        return ImagePair(a: a, b: b, matches: matches, lineMatches: lineMatches, diagnostics: diagnostics)
    }

    static func imageMatch(_ reader: inout UTF8JSONReader) throws -> ImageMatch {
        var a: [Double]?
        var b: [Double]?
        var score: Double?
        var manual: Bool?
        var geometricInlier: Bool?
        var kind: String?
        var lineA: [[Double]]?
        var lineB: [[Double]]?
        try reader.object { key, reader in
            switch key {
            case "a": a = try reader.array { reader in try reader.double() }
            case "b": b = try reader.array { reader in try reader.double() }
            case "score": score = try reader.optional { reader in try reader.double() }
            case "manual": manual = try reader.bool()
            case "geometricInlier": geometricInlier = try reader.optional { reader in try reader.bool() }
            case "kind": kind = try reader.optional { reader in try reader.string() }
            case "lineA":
                lineA = try reader.optional { reader in
                    try reader.array { reader in try reader.array { reader in try reader.double() } }
                }
            case "lineB":
                lineB = try reader.optional { reader in
                    try reader.array { reader in try reader.array { reader in try reader.double() } }
                }
            default: try reader.skipValue()
            }
        }
        guard let a, let b, let manual else { throw reader.invalid() }
        return ImageMatch(
            a: a, b: b, score: score, manual: manual, geometricInlier: geometricInlier, kind: kind, lineA: lineA,
            lineB: lineB)
    }

    static func lineMatch(_ reader: inout UTF8JSONReader) throws -> LineMatch {
        var indexA: Int?
        var indexB: Int?
        var startA: [Double]?
        var endA: [Double]?
        var startB: [Double]?
        var endB: [Double]?
        var score: Double?
        try reader.object { key, reader in
            switch key {
            case "indexA": indexA = try reader.integer()
            case "indexB": indexB = try reader.integer()
            case "startA": startA = try reader.array { reader in try reader.double() }
            case "endA": endA = try reader.array { reader in try reader.double() }
            case "startB": startB = try reader.array { reader in try reader.double() }
            case "endB": endB = try reader.array { reader in try reader.double() }
            case "score": score = try reader.double()
            default: try reader.skipValue()
            }
        }
        guard let indexA, let indexB, let startA, let endA, let startB, let endB, let score else {
            throw reader.invalid()
        }
        return LineMatch(
            indexA: indexA, indexB: indexB, startA: startA, endA: endA, startB: startB, endB: endB, score: score)
    }

    static func denseCloudReport(_ reader: inout UTF8JSONReader) throws -> DenseCloudReport {
        var inputSamples: Int?
        var retainedSamples: Int?
        var preview: [CloudPoint]?
        try reader.object { key, reader in
            switch key {
            case "inputSamples": inputSamples = try reader.integer()
            case "retainedSamples": retainedSamples = try reader.integer()
            case "preview": preview = try reader.array { reader in try cloudPoint(&reader) }
            default: try reader.skipValue()
            }
        }
        guard let inputSamples, let retainedSamples, let preview else { throw reader.invalid() }
        return DenseCloudReport(inputSamples: inputSamples, retainedSamples: retainedSamples, preview: preview)
    }

    static func fusedVolume(_ reader: inout UTF8JSONReader) throws -> FusedVolume {
        var voxelSize: Double?
        var samples: [VolumeSample]?
        try reader.object { key, reader in
            switch key {
            case "voxelSize": voxelSize = try reader.double()
            case "samples": samples = try reader.array { reader in try volumeSample(&reader) }
            default: try reader.skipValue()
            }
        }
        guard let voxelSize, let samples else { throw reader.invalid() }
        return FusedVolume(voxelSize: voxelSize, samples: samples)
    }

    static func volumeSample(_ reader: inout UTF8JSONReader) throws -> VolumeSample {
        var x: Int?
        var y: Int?
        var z: Int?
        var distance: Float?
        var views: Int?
        try reader.object { key, reader in
            switch key {
            case "x": x = try reader.integer()
            case "y": y = try reader.integer()
            case "z": z = try reader.integer()
            case "distance": distance = try reader.float()
            case "views": views = try reader.integer()
            default: try reader.skipValue()
            }
        }
        guard let x, let y, let z, let distance, let views else { throw reader.invalid() }
        return VolumeSample(x: x, y: y, z: z, distance: distance, views: views)
    }

    static func pairSummary(_ reader: inout UTF8JSONReader) throws -> ImagePairSummary {
        var a: String?
        var b: String?
        var count: Int?
        try reader.object { key, reader in
            switch key {
            case "a": a = try reader.string()
            case "b": b = try reader.string()
            case "matches": count = try reader.countArray()
            default: try reader.skipValue()
            }
        }
        guard let a, let b, let count else { throw reader.invalid() }
        return ImagePairSummary(a: a, b: b, count: count)
    }

    private static func data(_ reader: inout UTF8JSONReader) throws -> Data {
        guard let value = Data(base64Encoded: try reader.string()) else { throw reader.invalid() }
        return value
    }
}
