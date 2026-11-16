import Foundation

struct DepthStageQualitySummary {
    let mapCount: Int
    let maskedMapCount: Int
    let accepted: Int
    let total: Int
    let excludedSkyPixels: Int
    let isPreview: Bool

    init(maps: [DepthQualitySummary], isPreview: Bool) {
        mapCount = maps.count
        maskedMapCount = maps.filter(\.hasSkyMask).count
        accepted = maps.reduce(0) { $0 + $1.surfaceAccepted }
        total = maps.reduce(0) { $0 + $1.surfaceTotal }
        excludedSkyPixels = maps.reduce(0) { $0 + $1.excludedSkyPixels }
        self.isPreview = isPreview
    }

    var coverage: Double? { total > 0 ? Double(accepted) / Double(total) : nil }
    var grade: DepthHealthGrade? { coverage.map { DepthHealthGrade(coverage: $0) } }
    var label: String? {
        guard let grade, let coverage else { return nil }
        return "\(grade.rawValue) · \(coverage.formatted(.percent.precision(.fractionLength(1))))"
    }
    var detail: String {
        if mapCount == 0 { return "Awaiting depth maps" }
        if total == 0 { return "No surface pixels to grade" }
        return isPreview ? "Live coverage · \(mapCount.formatted()) maps" : "Overall depth coverage"
    }
    var help: String {
        let scope =
            isPreview ? "Live coverage of completed depth maps; updates as images finish." : "Overall depth coverage."
        let basis: String
        if maskedMapCount == mapCount, mapCount > 0 {
            basis = "Confident sky is excluded from all maps."
        } else if maskedMapCount > 0 {
            basis =
                "Confident sky is excluded from \(maskedMapCount) of \(mapCount) maps; the others use whole-image coverage."
        } else {
            basis = "Sky masks are unavailable; using whole-image coverage."
        }
        return
            "\(scope)\n\(mapCount.formatted()) maps · \(accepted.formatted()) / \(total.formatted()) eligible pixels have depth.\n"
            + "\(basis) Weighted by pixel count, not the average of image percentages.\n"
            + "Empty 0%, Poor <5%, Limited <25%, Partial <50%, Broad <65%, Strong <80%, Dense ≥80%.\n"
            + "Coverage describes completeness, not geometric accuracy or processing progress."
    }
}
