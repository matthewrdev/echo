import SwiftUI

struct ExclusionRegionEditor: View {
    @Binding var region: ExclusionRegion
    let bounds: SceneBounds
    let metricScale: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Toggle("Enable region", isOn: $region.enabled)
            Picker("Mode", selection: $region.mode) {
                ForEach(CropRegionMode.allCases, id: \.self) { Text($0.title).tag($0) }
            }.pickerStyle(.segmented)
            if region.shape == .projectedHull {
                Text("Painted polygon projected through the model from the view used to draw it.")
                    .font(.caption).foregroundStyle(.secondary)
                Text("Orbit to inspect its volume. Remove and repaint it to change the outline.")
                    .font(.caption).foregroundStyle(.secondary)
            } else {
                Picker("Shape", selection: $region.shape) {
                    ForEach([ExclusionShape.box, .cylinder], id: \.self) { Text($0.title).tag($0) }
                }.pickerStyle(.segmented)
                Text(metricScale ? "Position and size in metres" : "Position and size in model units")
                    .font(.caption).foregroundStyle(.secondary)
                coordinate("Centre X", value: $region.centreX, minimum: bounds.minimum.x, maximum: bounds.maximum.x)
                coordinate(
                    "Centre Y (up)", value: $region.centreY, minimum: bounds.minimum.y, maximum: bounds.maximum.y)
                coordinate("Centre Z", value: $region.centreZ, minimum: bounds.minimum.z, maximum: bounds.maximum.z)
                if region.shape == .box {
                    size("Width (X)", value: $region.width, extent: bounds.maximum.x - bounds.minimum.x)
                    size("Depth (Z)", value: $region.depth, extent: bounds.maximum.z - bounds.minimum.z)
                } else {
                    // A radius covering the horizontal bounding rectangle also covers its corners.
                    size(
                        "Radius", value: $region.radius,
                        extent: hypot(bounds.maximum.x - bounds.minimum.x, bounds.maximum.z - bounds.minimum.z) / 2)
                }
                size("Height (Y)", value: $region.height, extent: bounds.maximum.y - bounds.minimum.y)
                Text("Slider limits use the cloud size +20%. Type a larger value if needed.")
                    .font(.caption).foregroundStyle(.secondary)
                if region.shape == .box {
                    Divider()
                    adjustment("Rotation (°)", value: $region.rotationY, range: -180...180)
                    Text("Around the vertical axis").font(.caption).foregroundStyle(.secondary)
                }
            }
            if !region.isValid {
                Text("Enter finite coordinates and rotation, and positive dimensions.").font(.caption).foregroundStyle(
                    .red)
            }
        }
        .textFieldStyle(.roundedBorder)
    }

    private func coordinate(_ title: String, value: Binding<Double>, minimum: Double, maximum: Double) -> some View {
        adjustment(title, value: value, range: (minimum - bounds.span)...(maximum + bounds.span))
    }

    private func size(_ title: String, value: Binding<Double>, extent: Double) -> some View {
        // Keep a usable positive range for a flat cloud or a single point.
        let maximum = max(extent, bounds.span / 10_000) * 1.2
        return adjustment(title, value: value, range: (maximum / 10_000)...maximum)
    }

    private func adjustment(_ title: String, value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        VStack(spacing: 3) {
            dimension(title, value: value)
            // The text field permits exact values beyond the slider's useful scene range.
            Slider(
                value: Binding(
                    get: {
                        value.wrappedValue.isFinite
                            ? min(range.upperBound, max(range.lowerBound, value.wrappedValue)) : range.lowerBound
                    },
                    set: { value.wrappedValue = $0 }), in: range
            )
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
