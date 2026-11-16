import SwiftUI

struct RunLogRow: View, Equatable {
    let entry: RunLogEntry

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(entry.time).monospacedDigit().foregroundStyle(.secondary).fixedSize()
            Text(entry.message).textSelection(.enabled)
        }.frame(maxWidth: .infinity, alignment: .leading)
    }
}
