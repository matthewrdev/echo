import SwiftUI

struct ModelDependencyNotice: View {
    @State private var showsAttribution = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label("Optional model download · 228 MB", systemImage: "arrow.down.circle")
                .font(.callout.weight(.medium))
            Text(
                "These options use a pretrained model to identify sky, rock and vegetation. First use downloads it from Hugging Face. Echo shares the cached model across projects and processes photographs on this computer. Compatible photo classifications are also reused."
            )
            .font(.caption).foregroundStyle(.secondary)
            Text("The model declares an MIT license; commercial use of its trained weights remains unresolved.")
                .font(.caption).foregroundStyle(.secondary)
            Button("Model source, licenses and attribution…") { showsAttribution = true }
                .font(.caption)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.quaternary.opacity(0.4), in: RoundedRectangle(cornerRadius: 8))
        .sheet(isPresented: $showsAttribution) { AttributionView() }
    }
}
