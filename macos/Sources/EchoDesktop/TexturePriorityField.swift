import Foundation
import simd

struct TexturePriorityField: Sendable {
    private var entries: [TextureBrushEntry] = []
    private var nodes: [TextureBrushTreeNode] = []
    private let weights: [Double]

    init(strokes: [TexturePriorityStroke]) {
        weights = strokes.map { log2($0.weight) }
        for (index, stroke) in strokes.enumerated() where stroke.isValid {
            entries += stroke.samples.map { TextureBrushEntry(sample: $0, stroke: index) }
        }
        if !entries.isEmpty { _ = build(start: 0, count: entries.count) }
    }

    func logWeight(at point: SIMD3<Double>, influences: inout [Int: Double]) -> Double {
        influences.removeAll(keepingCapacity: true)
        if !nodes.isEmpty { query(0, point: point, influences: &influences) }
        var value = 0.0
        for index in influences.keys.sorted() {
            value += (weights[index] - value) * influences[index, default: 0]
        }
        return value
    }

    private mutating func build(start: Int, count: Int) -> Int {
        var minimum = SIMD3<Double>(repeating: .infinity)
        var maximum = SIMD3<Double>(repeating: -.infinity)
        for entry in entries[start..<(start + count)] {
            let sample = entry.sample
            let centre = SIMD3(sample.centre[0], sample.centre[1], sample.centre[2])
            minimum = simd_min(minimum, centre - sample.radius)
            maximum = simd_max(maximum, centre + sample.radius)
        }
        let index = nodes.count
        nodes.append(TextureBrushTreeNode(minimum: minimum, maximum: maximum, start: start, count: count))
        if count > 8 {
            let span = maximum - minimum
            let axis = span.x >= span.y && span.x >= span.z ? 0 : span.y >= span.z ? 1 : 2
            entries[start..<(start + count)].sort { $0.sample.centre[axis] < $1.sample.centre[axis] }
            let left = build(start: start, count: count / 2)
            let right = build(start: start + count / 2, count: count - count / 2)
            nodes[index].left = left
            nodes[index].right = right
        }
        return index
    }

    private func query(_ index: Int, point: SIMD3<Double>, influences: inout [Int: Double]) {
        let node = nodes[index]
        guard point.x >= node.minimum.x, point.x <= node.maximum.x,
            point.y >= node.minimum.y, point.y <= node.maximum.y,
            point.z >= node.minimum.z, point.z <= node.maximum.z
        else { return }
        if node.left >= 0 {
            query(node.left, point: point, influences: &influences)
            query(node.right, point: point, influences: &influences)
            return
        }
        for entry in entries[node.start..<(node.start + node.count)] {
            let influence = entry.sample.influence(at: point)
            if influence > 0 {
                influences[entry.stroke] = max(influences[entry.stroke, default: 0], influence)
            }
        }
    }
}
