import Foundation

final class FeatureMatchIndexer {
    private let json: ArtifactJSONReader
    private let index = MatchingIndexReader()

    init(json: ArtifactJSONReader) { self.json = json }

    func read(root: URL, run: RunData, features: FeatureSet) throws -> [FeatureMatchLink] {
        guard let matching = run.artifacts["matching"] else { return [] }
        let file = root.appendingPathComponent(matching).appendingPathComponent("pairs.ndjson")
        let units = try index.read(file)
        var lookup: [PixelCoordinate: Int] = [:]
        for (index, feature) in features.features.enumerated() {
            lookup[PixelCoordinate(x: feature.x, y: feature.y)] = index
        }
        let names = Dictionary(uniqueKeysWithValues: run.snapshot.images.map { ($0.id, $0.name) })
        var links: [FeatureMatchLink] = []
        for entry in units where entry.a == features.imageId || entry.b == features.imageId {
            let unit = entry.artifact
            let path = root.appendingPathComponent(unit).appendingPathComponent("pair.json")
            try Task.checkCancellation()
            let pair: ImagePair = try json.read(path)
            let isFirst = pair.a == features.imageId
            for (index, match) in (pair.lineMatches ?? []).enumerated() {
                let otherID = isFirst ? pair.b : pair.a
                let displayed = match.displayMatch
                links.append(
                    FeatureMatchLink(
                        unitID: unit, matchIndex: pair.matches.count + index,
                        featureIndex: features.features.count + (isFirst ? match.indexA : match.indexB),
                        otherPhotoName: names[otherID] ?? otherID, otherPixel: isFirst ? displayed.b : displayed.a,
                        score: match.score, manual: false))
            }
            for (index, match) in pair.matches.enumerated() {
                let pixel = isFirst ? match.a : match.b
                guard pixel.count == 2, let feature = lookup[PixelCoordinate(x: pixel[0], y: pixel[1])] else {
                    continue
                }
                let otherID = isFirst ? pair.b : pair.a
                links.append(
                    FeatureMatchLink(
                        unitID: unit, matchIndex: index, featureIndex: feature,
                        otherPhotoName: names[otherID] ?? otherID, otherPixel: isFirst ? match.b : match.a,
                        score: match.score, manual: match.manual))
            }
        }
        return links
    }
}
