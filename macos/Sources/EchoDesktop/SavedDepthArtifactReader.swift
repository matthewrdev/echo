import AppKit

actor SavedDepthArtifactReader {
    private let json: ArtifactJSONReader
    private let maps: DepthMapReader
    private let images: ArtifactImageRenderer
    private var cache: [CachedDepthArtifact] = []

    init(maps: DepthMapReader, images: ArtifactImageRenderer, json: ArtifactJSONReader = ArtifactJSONReader()) {
        self.json = json
        self.maps = maps
        self.images = images
    }

    func load(root: URL, run: RunData, unit: ArtifactUnit?, mode: String) throws -> ArtifactPresentation {
        try Task.checkCancellation()
        guard let unit, let photoID = unit.imageIDs.first,
            let photo = run.snapshot.images.first(where: { $0.id == photoID })
        else { throw CLIError(message: "Select a saved depth image.") }
        let photograph = root.appendingPathComponent(photo.source)
        let alignment = run.artifacts["alignment"].map {
            root.appendingPathComponent($0).appendingPathComponent("scene.json")
        }
        let semanticIndex = run.artifacts["semantics"].map {
            root.appendingPathComponent($0).appendingPathComponent("units.json")
        }
        let semanticUnits: [String: String]? = try readOptional(semanticIndex)
        let skyFile = semanticUnits?[photoID].map {
            root.appendingPathComponent($0).appendingPathComponent("semantics.json")
        }
        let files = ([unit.file, photograph] + [alignment, semanticIndex, skyFile].compactMap { $0 })
            .map { ArtifactFileVersion(file: $0) }
        let artifact: CachedDepthArtifact
        if let index = cache.firstIndex(where: { $0.files == files }) {
            artifact = cache.remove(at: index)
        } else {
            artifact = try read(
                file: unit.file, photograph: photograph, alignment: alignment, skyFile: skyFile, files: files)
        }
        guard artifact.depth.map.imageId == photoID else {
            throw CLIError(message: "Depth map does not match its photograph.")
        }
        try Task.checkCancellation()
        // Keep both display modes and a small set of recently inspected photos.
        // The maps and source photograph are shared by the two presentations.
        cache.insert(artifact, at: 0)
        if cache.count > 3 { cache.removeLast() }
        let depth = mode == "Confidence" ? artifact.confidence : artifact.depth
        let coverage = depth.quality.surfaceCoverage ?? depth.quality.coverage
        let coverageLabel = depth.quality.hasSkyMask ? "surface coverage" : "whole-image coverage"
        return ArtifactPresentation(
            title: photo.name,
            detail:
                "\(depth.map.width) × \(depth.map.height) · \(coverage.formatted(.percent.precision(.fractionLength(2)))) \(coverageLabel)",
            depthImage: depth)
    }

    private func read(file: URL, photograph: URL, alignment: URL?, skyFile: URL?, files: [ArtifactFileVersion]) throws
        -> CachedDepthArtifact
    {
        let map = try maps.read(file)
        let image = NSImage(contentsOf: photograph)
        let metadata: ReconstructionMetadata? = try readOptional(alignment)
        let sky: SkyProbabilityMap? = try readOptional(skyFile)
        let quality = DepthQualitySummary(map: map, sky: sky)
        let depth = try DepthImagePresentation(
            identity: file.path, overlay: images.depth(map, confidence: false), photograph: image,
            map: map, isMetric: metadata?.isMetric ?? false, quality: quality)
        let confidence = try DepthImagePresentation(
            identity: file.path, overlay: images.depth(map, confidence: true), photograph: image,
            map: map, isMetric: depth.isMetric, quality: depth.quality)
        return CachedDepthArtifact(files: files, depth: depth, confidence: confidence)
    }

    private func readOptional<T: Decodable>(_ file: URL?) throws -> T? {
        guard let file else { return nil }
        do {
            let value: T = try json.read(file)
            return value
        } catch is CancellationError {
            throw CancellationError()
        } catch {
            return nil
        }
    }
}
