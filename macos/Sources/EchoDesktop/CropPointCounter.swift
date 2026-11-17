import SceneKit

struct CropPointCounter: Sendable {
    let sources: [CropPointSource]

    @MainActor
    init(scene: SCNScene?) {
        var sources: [CropPointSource] = []
        for node in scene?.rootNode.childNodes ?? [] where node.name == "artifact" {
            if let source = CropPointSource(node: node) { sources.append(source) }
            node.enumerateChildNodes { child, _ in
                if let source = CropPointSource(node: child) { sources.append(source) }
            }
        }
        self.sources = sources
    }

    func count(regions: [ExclusionRegion]) throws -> CropPointCounts {
        let crop = RegionCropFilter(regions)
        var counts = CropPointCounts()
        for source in sources {
            counts.isMesh = counts.isMesh || source.isMesh
            try source.data.withUnsafeBytes { bytes in
                for index in 0..<source.count {
                    if index % 4096 == 0 { try Task.checkCancellation() }
                    let point = source.position(index, bytes: bytes)
                    guard point.x.isFinite, point.y.isFinite, point.z.isFinite else { continue }
                    if crop.isExcluded(point) {
                        counts.excluded += 1
                    } else {
                        counts.included += 1
                    }
                }
            }
        }
        return counts
    }
}
