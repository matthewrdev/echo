import Foundation

actor DepthStageQualityReader {
    private let maps: DepthMapReader
    private let json: ArtifactJSONReader
    private let preview = ImagePreviewIndex()
    private var root: URL?
    private var cache: [URL: CachedDepthQuality] = [:]

    init(maps: DepthMapReader = DepthMapReader(), json: ArtifactJSONReader = ArtifactJSONReader()) {
        self.maps = maps
        self.json = json
    }

    func load(_ request: DepthStageQualityRequest) throws -> DepthStageQualitySummary {
        try Task.checkCancellation()
        if root != request.root {
            cache.removeAll()
            root = request.root
        }
        let index = request.folder.appendingPathComponent("units.json")
        let paths: [String]
        if request.isPreview {
            let units = try preview.read(request.folder.appendingPathComponent("units.bin"))
            paths = Array(Set(units.values)).sorted()
        } else {
            let units: [String] = try json.read(index)
            paths = Array(Set(units)).sorted()
        }
        let skyUnits: [String: String] = optional(request.semanticsIndex) ?? [:]
        var summaries: [DepthQualitySummary] = []
        var used: Set<URL> = []
        for path in paths {
            try Task.checkCancellation()
            let file = request.root.appendingPathComponent(path).appendingPathComponent("depth.json")
            used.insert(file)
            let version = ArtifactFileVersion(file: file)
            if let cached = cache[file], cached.files.first == version {
                let sky = skyFile(imageID: cached.imageID, root: request.root, units: skyUnits)
                if cached.files == [version] + (sky.map { [ArtifactFileVersion(file: $0)] } ?? []) {
                    summaries.append(cached.summary)
                    continue
                }
            }
            // Keep only the small summaries. Full sample arrays are released after each map.
            let map = try maps.read(file)
            let sky = skyFile(imageID: map.imageId, root: request.root, units: skyUnits)
            let mask: SkyProbabilityMap? = optional(sky)
            let summary = DepthQualitySummary(map: map, sky: mask)
            let files = [version] + (sky.map { [ArtifactFileVersion(file: $0)] } ?? [])
            cache[file] = CachedDepthQuality(imageID: map.imageId, files: files, summary: summary)
            summaries.append(summary)
        }
        cache = cache.filter { used.contains($0.key) }
        try Task.checkCancellation()
        return DepthStageQualitySummary(maps: summaries, isPreview: request.isPreview)
    }

    private func skyFile(imageID: String, root: URL, units: [String: String]) -> URL? {
        units[imageID].map { root.appendingPathComponent($0).appendingPathComponent("semantics.json") }
    }

    private func optional<T: Decodable>(_ file: URL?) -> T? {
        guard let file else { return nil }
        do {
            let value: T = try json.read(file)
            return value
        } catch {
            return nil
        }
    }
}
