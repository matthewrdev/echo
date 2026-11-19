import SwiftUI

struct PagingSettingsEditor: View {
    @Binding var settings: ProcessingSettings

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Processing resources").font(.headline)
            Picker("Storage", selection: $settings.processingMemoryMode) {
                Text("Paged to disk").tag(ProcessingMemoryMode.paged)
                Text("In memory").tag(ProcessingMemoryMode.inMemory)
            }
            HStack {
                MemoryBudgetPicker(
                    title: "RAM budget", megabytes: $settings.processingMemoryBudgetMb, exponents: 8...18)
                Text("Scratch disk budget (MiB)")
                TextField("Available disk", value: $settings.processingDiskBudgetMb, format: .number)
                    .frame(width: 100)
            }
            Text(
                "Automatic allows 87.5% of the machine’s RAM, retaining system headroom. Memory grows as needed. Zero selects available scratch disk. Paging preserves reconstruction quality."
            )
            .font(.caption).foregroundStyle(.secondary)
        }
    }
}
