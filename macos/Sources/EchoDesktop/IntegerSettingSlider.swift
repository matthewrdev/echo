import SwiftUI

struct IntegerSettingSlider: View {
    let title: String
    @Binding var value: Int
    let range: ClosedRange<Int>
    var step: Double = 1

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                Spacer()
                TextField(title, value: $value, format: .number.grouping(.never))
                    .labelsHidden().multilineTextAlignment(.trailing).frame(width: 100)
                    .textFieldStyle(.roundedBorder).accessibilityLabel(title + " exact value")
            }
            Slider(
                value: Binding(get: { Double(value) }, set: { value = Int($0.rounded()) }),
                in: Double(range.lowerBound)...Double(range.upperBound), step: step
            )
            .accessibilityLabel(title)
            HStack {
                Text(range.lowerBound.formatted())
                Spacer()
                Text(range.upperBound.formatted())
            }.font(.caption2).foregroundStyle(.secondary)
        }.padding(.vertical, 4)
    }
}
