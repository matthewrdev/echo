import SwiftUI

struct DecimalSettingSlider: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let step: Double

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                Spacer()
                TextField(title, value: $value, format: .number.grouping(.never))
                    .labelsHidden().multilineTextAlignment(.trailing).frame(width: 100)
                    .textFieldStyle(.roundedBorder).accessibilityLabel(title + " exact value")
            }
            Slider(value: $value, in: range, step: step).accessibilityLabel(title)
            HStack {
                Text(range.lowerBound.formatted())
                Spacer()
                Text(range.upperBound.formatted())
            }.font(.caption2).foregroundStyle(.secondary)
        }.padding(.vertical, 4)
    }
}
