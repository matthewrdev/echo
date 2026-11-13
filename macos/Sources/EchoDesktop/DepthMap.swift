import Foundation

struct DepthMap: Decodable {
    let imageId: String
    let width: Int
    let height: Int
    let depth: [Float]
    let confidence: [Float]

    func sample(at point: CGPoint) -> DepthSample? {
        guard point.x.isFinite, point.y.isFinite,
            point.x >= 0, point.y >= 0, point.x < CGFloat(width), point.y < CGFloat(height)
        else { return nil }
        let x = Int(point.x)
        let y = Int(point.y)
        let row = y.multipliedReportingOverflow(by: width)
        guard !row.overflow else { return nil }
        let offset = row.partialValue.addingReportingOverflow(x)
        guard !offset.overflow, depth.indices.contains(offset.partialValue),
            confidence.indices.contains(offset.partialValue)
        else { return nil }
        let value = depth[offset.partialValue]
        let score = confidence[offset.partialValue]
        let accepted = value.isFinite && value > 0
        return DepthSample(
            x: x, y: y, depth: accepted ? value : nil,
            confidence: accepted && score.isFinite ? score : nil)
    }
}
