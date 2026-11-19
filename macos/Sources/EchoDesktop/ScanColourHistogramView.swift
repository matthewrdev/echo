import SwiftUI

struct ScanColourHistogramView: View {
    let histogram: ColourHistogram
    var logarithmic = false
    @State private var hovered: Int?

    private var bins: [ScanColourBin] {
        histogram.scanColours.filter { $0.count > 0 }.sorted {
            $0.sortOrder == $1.sortOrder ? $0.id < $1.id : $0.sortOrder < $1.sortOrder
        }
    }

    var body: some View {
        let colours = bins
        VStack(alignment: .leading, spacing: 6) {
            GeometryReader { geometry in
                let peak = colours.map(\.count).max() ?? 0
                HStack(alignment: .bottom, spacing: 1) {
                    ForEach(colours) { bin in
                        Rectangle()
                            .fill(
                                Color(
                                    .sRGB, red: Double(bin.colour.x) / 255, green: Double(bin.colour.y) / 255,
                                    blue: Double(bin.colour.z) / 255, opacity: 1)
                            )
                            .frame(maxWidth: .infinity)
                            .frame(
                                height: peak == 0 ? 0 : max(1, geometry.size.height * scale(bin.count) / scale(peak))
                            )
                            .overlay(
                                Rectangle().strokeBorder(
                                    .primary.opacity(hovered == bin.id ? 0.8 : 0.12), lineWidth: 0.5)
                            )
                            .contentShape(Rectangle())
                            .onHover { hovered = $0 ? bin.id : nil }
                            .help(description(bin))
                            .accessibilityLabel(description(bin))
                    }
                }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomLeading)
            }
            .frame(height: 130)
            .padding(4)
            .background(.quaternary.opacity(0.4), in: RoundedRectangle(cornerRadius: 4))
            Text(
                colours.first(where: { $0.id == hovered }).map(description)
                    ?? "\(histogram.count.formatted()) points"
            )
            .font(.caption2).monospacedDigit().foregroundStyle(.secondary)
            .lineLimit(1).frame(height: 16, alignment: .leading)
        }
        .help(
            "Colour frequency across every point in the selected region passing Depth agreement. Hover over a bar for its colour and share."
        )
        .accessibilityIdentifier("colourGrade.scanColours")
    }

    private func scale(_ value: UInt64) -> Double { logarithmic ? log1p(Double(value)) : Double(value) }

    private func description(_ bin: ScanColourBin) -> String {
        let share = histogram.count == 0 ? 0 : Double(bin.count) / Double(histogram.count)
        return
            "\(bin.hex) · \(bin.count.formatted()) points · \(share.formatted(.percent.precision(.fractionLength(1))))"
    }
}
