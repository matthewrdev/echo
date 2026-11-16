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
                "Automatic RAM leaves headroom for other applications. Zero selects available scratch disk. Paging preserves the requested reconstruction resolution."
            )
            .font(.caption).foregroundStyle(.secondary)
        }
    }
}
