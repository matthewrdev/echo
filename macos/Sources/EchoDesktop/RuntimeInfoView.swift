import SwiftUI

struct RuntimeInfoView: View {
    @ObservedObject var model: RuntimeInfoViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(nsImage: NSApplication.shared.applicationIconImage).resizable().frame(width: 44, height: 44)
                Text("About Echo").font(.title2.bold())
                Spacer()
                if model.isLoading { ProgressView().controlSize(.small) }
            }.padding(20)
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    ForEach(model.sections) { section in
                        VStack(alignment: .leading, spacing: 10) {
                            Text(section.title).font(.headline)
                            Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 7) {
                                ForEach(section.fields) { field in
                                    GridRow(alignment: .top) {
                                        Text(field.name).foregroundStyle(.secondary).frame(
                                            width: 150, alignment: .leading)
                                        Text(field.value).textSelection(.enabled).frame(
                                            maxWidth: .infinity, alignment: .leading)
                                    }
                                }
                            }.font(.callout)
                        }
                    }
                    Text(
                        "Processor details are queried from a separate diagnostic process using the configured executable. Memory and uptime above belong to this desktop process."
                    )
                    .font(.caption).foregroundStyle(.secondary)
                    if let error = model.error {
                        Label("Processor information unavailable: \(error)", systemImage: "exclamationmark.triangle")
                            .font(.callout).foregroundStyle(.orange).textSelection(.enabled)
                    }
                }.padding(20)
            }
            Divider()
            HStack {
                Button(model.copied ? "Copied" : "Copy report", systemImage: model.copied ? "checkmark" : "doc.on.doc")
                { model.copy() }
                .disabled(model.sections.isEmpty || model.isLoading)
                Button("Refresh", systemImage: "arrow.clockwise") { Task { await model.refresh() } }
                    .disabled(model.isLoading)
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }.padding(16)
        }
        .frame(width: 720, height: 640)
        .task { await model.refresh() }
    }
}
