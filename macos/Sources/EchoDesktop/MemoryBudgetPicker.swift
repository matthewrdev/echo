import SwiftUI

struct MemoryBudgetPicker: View {
    let title: String
    @Binding var megabytes: Int
    let exponents: ClosedRange<Int>

    private var choices: [Int] { exponents.map { 1 << $0 } }

    var body: some View {
        Picker(title, selection: $megabytes) {
            Text("Automatic").tag(0)
            ForEach(choices, id: \.self) { value in
                Text(label(value)).tag(value)
            }
            if megabytes != 0, !choices.contains(megabytes) {
                Text("\(label(megabytes)) (saved custom limit)").tag(megabytes)
            }
        }
    }

    private func label(_ value: Int) -> String {
        value >= 1024 && value % 1024 == 0 ? "\(value / 1024) GiB" : "\(value) MiB"
    }
}
