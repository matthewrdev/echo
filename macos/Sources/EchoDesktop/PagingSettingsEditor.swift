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
                Text("RAM budget (MiB)")
                TextField("Automatic", value: $settings.processingMemoryBudgetMb, format: .number)
                    .frame(width: 100)
                Text("Scratch disk budget (MiB)")
                TextField("Available disk", value: $settings.processingDiskBudgetMb, format: .number)
                    .frame(width: 100)
            }
            Text(
                "Zero selects automatic RAM or available disk. Paging preserves the requested reconstruction resolution."
            )
            .font(.caption).foregroundStyle(.secondary)
        }
    }
}
