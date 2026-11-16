import SwiftUI

struct PerformanceRunGraph: View {
    let document: PerformanceTraceDocument
    let select: (PipelineStage) -> Void

    private var nodes: [PerformanceGraphNode] { PerformanceGraphNode.make(document) }
    private let cardHeight: CGFloat = 144
    private let gap: CGFloat = 30

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Follow the arrows from preparation to the outcome. Select a processing node to inspect its timings.")
                .font(.callout).foregroundStyle(.secondary)
            GeometryReader { bounds in
                let columns = max(1, min(3, Int(bounds.size.width / 290)))
                let width = (bounds.size.width - gap * CGFloat(columns - 1) - 16) / CGFloat(columns)
                let rows = (nodes.count + columns - 1) / columns
                ScrollView {
                    ZStack(alignment: .topLeading) {
                        Canvas { context, _ in
                            for index in 1..<nodes.count {
                                let previous = centre(index - 1, columns: columns, width: width)
                                let current = centre(index, columns: columns, width: width)
                                let down = current.y > previous.y
                                let direction: CGFloat = current.x > previous.x ? 1 : -1
                                let start = CGPoint(
                                    x: previous.x + (down ? 0 : direction * width / 2),
                                    y: previous.y + (down ? cardHeight / 2 : 0))
                                let end = CGPoint(
                                    x: current.x - (down ? 0 : direction * width / 2),
                                    y: current.y - (down ? cardHeight / 2 : 0))
                                var line = Path()
                                line.move(to: start)
                                line.addLine(to: end)
                                context.stroke(line, with: .color(.secondary.opacity(0.6)), lineWidth: 2)
                                var arrow = Path()
                                arrow.move(
                                    to: CGPoint(x: end.x - (down ? 5 : direction * 7), y: end.y - (down ? 7 : 5)))
                                arrow.addLine(to: end)
                                arrow.addLine(
                                    to: CGPoint(x: end.x + (down ? 5 : -direction * 7), y: end.y + (down ? -7 : 5)))
                                context.stroke(arrow, with: .color(.secondary), lineWidth: 2)
                            }
                        }
                        ForEach(nodes.indices, id: \.self) { index in
                            let node = nodes[index]
                            Button {
                                if let stage = node.stage { select(stage) }
                            } label: {
                                card(node, number: index + 1)
                            }
                            .buttonStyle(.plain)
                            .allowsHitTesting(node.stage != nil)
                            .accessibilityLabel("\(index + 1). \(node.title), \(node.status.rawValue), \(node.detail)")
                            .frame(width: width, height: cardHeight)
                            .position(centre(index, columns: columns, width: width))
                        }
                    }
                    .frame(height: CGFloat(rows) * (cardHeight + gap) - gap + 16)
                }
            }
            Text(
                "Built/reused counts refer to checkpoints. Arrows show stage order; work inside a stage may run concurrently."
            )
            .font(.caption).foregroundStyle(.secondary)
        }
    }

    private func centre(_ index: Int, columns: Int, width: CGFloat) -> CGPoint {
        let row = index / columns
        let column = row % 2 == 0 ? index % columns : columns - 1 - index % columns
        return CGPoint(
            x: 8 + width / 2 + CGFloat(column) * (width + gap),
            y: 8 + cardHeight / 2 + CGFloat(row) * (cardHeight + gap))
    }

    private func color(_ status: PerformanceStatus) -> Color {
        switch status {
        case .completed: return .green
        case .reused: return .teal
        case .running: return .blue
        case .failed: return .red
        case .cancelled, .interrupted: return .orange
        }
    }

    private func card(_ node: PerformanceGraphNode, number: Int) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .top) {
                Label(node.title, systemImage: node.symbol).font(.headline).lineLimit(2)
                Spacer(minLength: 4)
                Text("\(number)").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            }
            HStack {
                Text(node.status.rawValue.capitalized).foregroundStyle(color(node.status))
                Spacer()
                Text(PerformanceDuration.format(node.milliseconds)).monospacedDigit()
            }.font(.callout)
            Text(node.detail).font(.caption)
            Text(node.work).font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading).padding(14)
        .background(color(node.status).opacity(0.055), in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(color(node.status).opacity(0.5), lineWidth: 1))
        .contentShape(RoundedRectangle(cornerRadius: 12))
    }
}
