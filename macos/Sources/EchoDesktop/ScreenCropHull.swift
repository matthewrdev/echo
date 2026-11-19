import Foundation

struct ScreenCropHull {
    func adding(_ point: CGPoint, radius: Double, to previous: [CGPoint]) -> [CGPoint] {
        let circle = (0..<16).map { index in
            let angle = Double(index) * .pi / 8
            return CGPoint(x: point.x + cos(angle) * radius, y: point.y + sin(angle) * radius)
        }
        let hull = convex(previous + circle)
        guard hull.count > 128 else { return hull }
        return (0..<128).map { hull[$0 * hull.count / 128] }
    }

    func convex(_ points: [CGPoint]) -> [CGPoint] {
        let sorted = points.sorted { $0.x == $1.x ? $0.y < $1.y : $0.x < $1.x }
        guard sorted.count >= 3 else { return sorted }
        func chain(_ values: [CGPoint]) -> [CGPoint] {
            var result: [CGPoint] = []
            for point in values {
                while result.count >= 2 {
                    let a = result[result.count - 2]
                    let b = result[result.count - 1]
                    let cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x)
                    if cross > 0.000001 { break }
                    result.removeLast()
                }
                result.append(point)
            }
            return result
        }
        return Array(chain(sorted).dropLast()) + Array(chain(sorted.reversed()).dropLast())
    }
}
