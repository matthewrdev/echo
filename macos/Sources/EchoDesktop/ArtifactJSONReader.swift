import Foundation
import stdlib_h
import xlocale

final class ArtifactJSONReader {
    private let cache = NSCache<NSString, CachedJSONArtifact>()

    init() {
        cache.totalCostLimit = 128 * 1024 * 1024
        cache.countLimit = 4096
    }

    func read<T: Decodable>(_ file: URL) throws -> T {
        try Task.checkCancellation()
        let version = ArtifactFileVersion(file: file)
        let key = "\(T.self):\(file.path)" as NSString
        if let cached = cache.object(forKey: key), cached.version == version, let value = cached.value as? T {
            return value
        }
        let data = try Data(contentsOf: file, options: .mappedIfSafe)
        let value: T = try decode(data)
        // Summaries retain no sample arrays. Reserve more than the input size for decoded nested arrays.
        let cost = T.self == ImagePairSummary.self || T.self == ReconstructionMetadata.self ? 4096 : data.count * 2
        cache.setObject(CachedJSONArtifact(version: version, value: value), forKey: key, cost: cost)
        return value
    }

    func decode<T: Decodable>(_ data: Data) throws -> T {
        guard let locale = newlocale(LC_NUMERIC_MASK, "C", nil) else {
            throw CLIError(message: "Cannot create the JSON numeric locale.")
        }
        defer { freelocale(locale) }
        return try data.withUnsafeBytes { raw in
            var reader = UTF8JSONReader(bytes: raw.bindMemory(to: UInt8.self), locale: locale)
            let value: Any
            switch T.self {
            case is ReconstructionScene.Type: value = try ArtifactJSONParser.reconstructionScene(&reader)
            case is ReconstructionMetadata.Type: value = try ArtifactJSONParser.reconstructionMetadata(&reader)
            case is CloudPoint.Type: value = try ArtifactJSONParser.cloudPoint(&reader)
            case is Surface.Type: value = try ArtifactJSONParser.surface(&reader)
            case is TextureArtifact.Type: value = try ArtifactJSONParser.textureArtifact(&reader)
            case is FeatureSet.Type: value = try ArtifactJSONParser.featureSet(&reader)
            case is ImageFeature.Type: value = try ArtifactJSONParser.imageFeature(&reader)
            case is LineFeature.Type: value = try ArtifactJSONParser.lineFeature(&reader)
            case is ImagePair.Type: value = try ArtifactJSONParser.imagePair(&reader)
            case is ImageMatch.Type: value = try ArtifactJSONParser.imageMatch(&reader)
            case is LineMatch.Type: value = try ArtifactJSONParser.lineMatch(&reader)
            case is DenseCloudReport.Type: value = try ArtifactJSONParser.denseCloudReport(&reader)
            case is FusedVolume.Type: value = try ArtifactJSONParser.fusedVolume(&reader)
            case is VolumeSample.Type: value = try ArtifactJSONParser.volumeSample(&reader)
            case is ImagePairSummary.Type: value = try ArtifactJSONParser.pairSummary(&reader)
            default: return try JSONDecoder().decode(T.self, from: data)
            }
            try reader.finish()
            guard let result = value as? T else { throw reader.invalid() }
            return result
        }
    }
}
