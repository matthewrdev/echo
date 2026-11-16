import Foundation

struct DepthQualitySummary {
    let accepted: Int
    let total: Int
    let meanConfidence: Double?
    let coveredRegions: Int
    let regionCount: Int
    let excludedSkyPixels: Int
    let surfaceAccepted: Int
    let hasSkyMask: Bool

    var coverage: Double { total > 0 ? Double(accepted) / Double(total) : 0 }
    var surfaceTotal: Int { total - excludedSkyPixels }
    var surfaceCoverage: Double? {
        guard hasSkyMask else { return nil }
        return surfaceTotal > 0 ? Double(surfaceAccepted) / Double(surfaceTotal) : 0
    }
    var grade: DepthHealthGrade { DepthHealthGrade(coverage: surfaceCoverage ?? coverage) }

    init(map: DepthMap, sky: SkyProbabilityMap? = nil) {
        let size = map.width.multipliedReportingOverflow(by: map.height)
        guard map.width > 0, map.height > 0, !size.overflow, size.partialValue == map.depth.count else {
            accepted = 0
            total = 0
            meanConfidence = nil
            coveredRegions = 0
            regionCount = 0
            excludedSkyPixels = 0
            surfaceAccepted = 0
            hasSkyMask = false
            return
        }
        let sky = sky.flatMap { $0.matches(map) ? $0 : nil }
        let skyCells = sky?.confidentCells() ?? []
        let skyColumns = sky.map { Self.sampleCoordinates(count: map.width, maskSize: $0.width) } ?? []
        let skyRows = sky.map { Self.sampleCoordinates(count: map.height, maskSize: $0.height) } ?? []
        var excludedCount = 0
        var surfaceCount = 0
        var acceptedCount = 0
        var confidenceCount = 0
        var confidenceSum = 0.0
        var regionSamples = [Int](repeating: 0, count: 48)
        var regionTotals = regionSamples
        for y in 0..<map.height {
            let regionRow = y * 6 / map.height * 8
            for x in 0..<map.width {
                let region = regionRow + x * 8 / map.width
                let index = y * map.width + x
                let depth = map.depth[index]
                let accepted = depth.isFinite && depth > 0
                if accepted { acceptedCount += 1 }
                if let sky {
                    if skyCells[skyRows[y] * sky.width + skyColumns[x]] {
                        excludedCount += 1
                        continue
                    }
                }
                regionTotals[region] += 1
                guard accepted else { continue }
                surfaceCount += 1
                regionSamples[region] += 1
                if map.confidence.indices.contains(index) {
                    let confidence = map.confidence[index]
                    if confidence.isFinite, (0...1).contains(confidence) {
                        confidenceSum += Double(confidence)
                        confidenceCount += 1
                    }
                }
            }
        }
        var populated = 0
        var covered = 0
        for index in regionTotals.indices where regionTotals[index] > 0 {
            populated += 1
            if Double(regionSamples[index]) / Double(regionTotals[index]) >= 0.1 { covered += 1 }
        }
        accepted = acceptedCount
        total = map.depth.count
        meanConfidence = confidenceCount > 0 ? confidenceSum / Double(confidenceCount) : nil
        coveredRegions = covered
        regionCount = populated
        excludedSkyPixels = excludedCount
        surfaceAccepted = surfaceCount
        hasSkyMask = sky != nil
    }

    private static func sampleCoordinates(count: Int, maskSize: Int) -> [Int] {
        (0..<count).map { coordinate in
            let normalized = (Double(coordinate) + 0.5) / Double(count)
            let projected = Int(floor(normalized * Double(maskSize) - 0.5))
            return min(maskSize - 1, max(0, projected))
        }
    }
}
