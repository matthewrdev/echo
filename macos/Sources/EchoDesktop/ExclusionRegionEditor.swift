import SwiftUI

struct ExclusionRegionEditor: View {
    @Binding var region: ExclusionRegion
    let bounds: SceneBounds
    let metricScale: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Toggle("Exclude this region", isOn: $region.enabled)
            Picker("Shape", selection: $region.shape) {
                ForEach(ExclusionShape.allCases, id: \.self) { Text($0.title).tag($0) }
            }.pickerStyle(.segmented)
            Text(metricScale ? "Position and size in metres" : "Position and size in model units")
                .font(.caption).foregroundStyle(.secondary)
            coordinate("Centre X", value: $region.centreX, minimum: bounds.minimum.x, maximum: bounds.maximum.x)
            coordinate("Centre Y (up)", value: $region.centreY, minimum: bounds.minimum.y, maximum: bounds.maximum.y)
            coordinate("Centre Z", value: $region.centreZ, minimum: bounds.minimum.z, maximum: bounds.maximum.z)
            if region.shape == .box {
                dimension("Width (X)", value: $region.width)
                dimension("Depth (Z)", value: $region.depth)
            } else {
                dimension("Radius", value: $region.radius)
            }
            dimension("Height (Y)", value: $region.height)
            if !region.isValid {
                Text("Enter finite coordinates and positive dimensions.").font(.caption).foregroundStyle(.red)
            }
        }
        .textFieldStyle(.roundedBorder)
    }

    private func coordinate(_ title: String, value: Binding<Double>, minimum: Double, maximum: Double) -> some View {
        VStack(spacing: 3) {
            dimension(title, value: value)
            Slider(value: value, in: (minimum - bounds.span)...(maximum + bounds.span))
                .accessibilityLabel(title)
        }
    }

    private func dimension(_ title: String, value: Binding<Double>) -> some View {
        HStack {
            Text(title).font(.caption)
            Spacer()
            TextField(title, value: value, format: .number.grouping(.never))
                .labelsHidden().frame(width: 110).accessibilityLabel(title)
        }
    }
}
