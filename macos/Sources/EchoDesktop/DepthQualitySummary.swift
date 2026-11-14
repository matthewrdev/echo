import Foundation

struct DepthQualitySummary {
    let accepted: Int
    let total: Int
    let meanConfidence: Double?
    let coveredRegions: Int
    let regionCount: Int

    var coverage: Double { total > 0 ? Double(accepted) / Double(total) : 0 }
    var grade: DepthHealthGrade { DepthHealthGrade(coverage: coverage) }

    init(map: DepthMap) {
        let size = map.width.multipliedReportingOverflow(by: map.height)
        guard map.width > 0, map.height > 0, !size.overflow, size.partialValue == map.depth.count else {
            accepted = 0
            total = 0
            meanConfidence = nil
            coveredRegions = 0
            regionCount = 0
            return
        }
        var acceptedCount = 0
        var confidenceCount = 0
        var confidenceSum = 0.0
        var regionSamples = [Int](repeating: 0, count: 48)
        var regionTotals = regionSamples
        for y in 0..<map.height {
            let regionRow = y * 6 / map.height * 8
            for x in 0..<map.width {
                let region = regionRow + x * 8 / map.width
                regionTotals[region] += 1
                let index = y * map.width + x
                let depth = map.depth[index]
                guard depth.isFinite, depth > 0 else { continue }
                acceptedCount += 1
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
    }
}
