import Foundation

struct SparsePointPreview {
    let points: [CloudPoint]
    let eligibleCount: Int

    init(points: [CloudPoint], options: SparseDisplayOptions) {
        let eligible = points.filter { point in
            guard point.position.count == 3, point.position.allSatisfy(\.isFinite) else { return false }
            guard let maximum = options.maximumReprojectionError else { return true }
            return point.error.isFinite && point.error >= 0 && point.error <= maximum
        }
        eligibleCount = eligible.count
        let count = options.detail.limit(for: eligible.count)
        if count == eligible.count {
            self.points = eligible
        } else {
            self.points = (0..<count).map { eligible[$0 * eligible.count / count] }
        }
    }
}
