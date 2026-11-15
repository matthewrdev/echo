import SwiftUI

struct FusionLegendView: View {
    let info: FusionPresentationInfo

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text("Combined depth observations define the surface. Mesh builds the final triangles from this volume.")
                .font(.callout).foregroundStyle(.secondary)
            HStack(spacing: 16) {
                Text("\(info.samples.formatted()) grid samples · \(info.spacing) spacing")
                if info.mode == .support {
                    swatch("2 views", color: .orange)
                    swatch("8", color: .mint)
                    swatch("16+", color: .blue)
                    Text("Minimum support per surface cell").foregroundStyle(.secondary)
                } else if let position = info.slicePosition {
                    Text(
                        "\(info.mode.axis ?? "") = \(position.formatted(.number.precision(.fractionLength(3)))) \(info.units)"
                    )
                    swatch("Behind", color: .blue)
                    swatch("Surface", color: Color(white: 0.55))
                    swatch("In front", color: .orange)
                } else {
                    Text("Surface preview · drag to orbit · scroll to zoom").foregroundStyle(.secondary)
                }
            }.font(.caption)
            if let count = info.sliceSamples {
                Text(
                    count == 0
                        ? "No observed samples on this slice. Move the slider to explore the volume."
                        : "\(count.formatted()) samples on this plane · blank regions have no saved observations"
                )
                .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func swatch(_ label: String, color: Color) -> some View {
        HStack(spacing: 4) {
            Circle().fill(color).frame(width: 8, height: 8)
            Text(label)
        }
    }
}
