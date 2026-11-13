import SwiftUI

struct MetadataSamplePlot: View {
    let values: [Double]
    var sampleOffset = 0

    var body: some View {
        let finite = values.filter(\.isFinite)
        let minimum = finite.min() ?? 0
        let maximum = finite.max() ?? 0
        VStack(alignment: .leading) {
            Text("\(values.count) samples · min \(minimum.formatted()) · max \(maximum.formatted())")
                .font(.caption).textSelection(.enabled)
            Canvas { context, size in
                var path = Path()
                var connected = false
                for (index, value) in values.enumerated() {
                    guard value.isFinite else {
                        connected = false
                        continue
                    }
                    let x = Double(index) / Double(max(1, values.count - 1)) * size.width
                    let y = size.height * (1 - (value - minimum) / max(maximum - minimum, 1e-20))
                    let point = CGPoint(x: x, y: y)
                    if connected { path.addLine(to: point) } else { path.move(to: point) }
                    connected = true
                }
                context.stroke(path, with: .color(.accentColor), lineWidth: 1)
            }.background(.quaternary.opacity(0.3)).frame(maxHeight: .infinity)
            HStack {
                Text("Sample \(sampleOffset)")
                Spacer()
                Text("Sample \(sampleOffset + max(0, values.count - 1))")
            }.font(.caption2)
        }
    }
}
