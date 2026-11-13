import SwiftUI

struct LineFeatureInspector: View {
    let data: FeaturePresentation
    let index: Int
    let openMatch: (FeatureMatchLink) -> Void

    var body: some View {
        let line = data.lines[index]
        let matches = data.matches.filter { $0.featureIndex == data.features.count + index }
        VStack(alignment: .leading, spacing: 12) {
            Text("Line \(index + 1)").font(.headline)
            Text("\(line.length.formatted(.number.precision(.fractionLength(1)))) px long").font(.caption)
            Text(
                "Start: \(line.start[0].formatted()), \(line.start[1].formatted())\nEnd: \(line.end[0].formatted()), \(line.end[1].formatted())"
            )
            .font(.caption).textSelection(.enabled)
            MatchPatchView(
                title: "Selected line", image: data.image, photo: data.photo, pixel: line.midpoint,
                overlay: .line(
                    start: CGPoint(x: line.start[0], y: line.start[1]),
                    end: CGPoint(x: line.end[0], y: line.end[1]))
            )
            .frame(height: 200)
            Text("\(matches.count) candidate line matches").font(.subheadline.bold())
            Text(
                "Straight edge fitted to image gradients. Line candidates are for inspection; the solver uses matched junction points, not segment endpoints or standalone 3D lines."
            )
            .font(.caption).foregroundStyle(.secondary)
            ForEach(matches) { match in
                Text(match.otherPhotoName).font(.caption)
                Button("Compare this line →") { openMatch(match) }
                Divider()
            }
            DisclosureGroup("Descriptor values") {
                Text(
                    line.descriptor.map { $0.formatted(.number.precision(.fractionLength(3))) }.joined(separator: ", ")
                )
                .font(.system(.caption2, design: .monospaced)).textSelection(.enabled)
            }
        }
    }
}
