import SwiftUI

struct ColourHistogramView: View {
    let histogram: ColourHistogram
    var logarithmic = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ScanColourHistogramView(histogram: histogram, logarithmic: logarithmic)
            DisclosureGroup("Channels") {
                VStack(alignment: .leading, spacing: 6) {
                    Text("RGB channels").font(.caption).foregroundStyle(.secondary)
                    plot(channels: [histogram.red, histogram.green, histogram.blue], colours: [.red, .green, .blue])
                        .frame(height: 90)
                    Text("Brightness").font(.caption).foregroundStyle(.secondary)
                    plot(channels: [histogram.brightness], colours: [.primary]).frame(height: 60)
                    HStack {
                        Text("0 · black")
                        Spacer()
                        Text("white · 255")
                    }.font(.caption2).foregroundStyle(.secondary)
                }
            }.font(.caption)
        }
    }

    private func plot(channels: [[UInt64]], colours: [Color]) -> some View {
        Canvas { context, size in
            let peak = channels.flatMap { $0 }.max() ?? 0
            let denominator = scale(peak)
            guard denominator > 0 else { return }
            for (index, bins) in channels.enumerated() {
                var path = Path()
                for bin in bins.indices {
                    let point = CGPoint(
                        x: Double(bin) / 255 * size.width,
                        y: size.height * (1 - scale(bins[bin]) / denominator))
                    if bin == 0 { path.move(to: point) } else { path.addLine(to: point) }
                }
                context.stroke(path, with: .color(colours[index].opacity(0.85)), lineWidth: 1)
            }
        }
        .background(.quaternary.opacity(0.4), in: RoundedRectangle(cornerRadius: 4))
    }

    private func scale(_ count: UInt64) -> Double { logarithmic ? log1p(Double(count)) : Double(count) }
}
